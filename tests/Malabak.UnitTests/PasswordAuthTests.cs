using System.Text;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.PasswordAuth;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

public sealed class PasswordAuthTests
{
    private const string _email = "owner@club.com";
    private const string _password = "green pitch at midnight";
    private const string _tooShortPassphrase = "short";

    private readonly MutableClock _clock = new();
    private readonly RecordingEmailSender _mailbox = new();
    private readonly RecordingPhoneAlerts _phone = new();
    private readonly FakeCipher _cipher = new();
    private readonly Pbkdf2PasswordHasher _hasher = new();
    private readonly TotpService _totp = new();
    private readonly AllowTurnstile _turnstile = new();

    // ---------- Hashing & policy ----------

    [Fact]
    public void Hasher_RoundTrips_AndRejectsWrongPassword()
    {
        var hash = _hasher.Hash(_password);

        hash.Should().StartWith("pbkdf2-sha256$600000$");
        _hasher.Verify(hash, _password).Should().Be(PasswordVerification.Success);
        _hasher.Verify(hash, "wrong password!").Should().Be(PasswordVerification.Failed);
        _hasher.Verify("garbage", _password).Should().Be(PasswordVerification.Failed);
    }

    [Fact]
    public void Hasher_WeakerIterations_FlagsRehash()
    {
        var salt = new byte[16];
        var weak = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(_password, salt, 1000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        var stored = $"pbkdf2-sha256$1000${Convert.ToBase64String(salt)}${Convert.ToBase64String(weak)}";

        _hasher.Verify(stored, _password).Should().Be(PasswordVerification.SuccessRehashNeeded);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("1234567890")]
    [InlineData("aaaaaaaaaaaa")]
    [InlineData("owner-secret-123")]
    public async Task Policy_RejectsWeakPasswordsAsync(string password)
    {
        var errors = await new PasswordPolicy(new FakeBreachChecker(false)).ValidateAsync(password, _email);

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Policy_RejectsBreachedPassword_AcceptsLongUniqueOneAsync()
    {
        (await new PasswordPolicy(new FakeBreachChecker(true)).ValidateAsync(_password, _email)).Should().NotBeEmpty();
        (await new PasswordPolicy(new FakeBreachChecker(false)).ValidateAsync(_password, _email)).Should().BeEmpty();
    }

    [Fact]
    public void Hibp_RangeParsing_MatchesSuffixWithPositiveCount()
    {
        const string response = "0018A45C4D1DEF81644B54AB7F969B88D65:1\r\n00D4F6E8FA6EECAD2A3AA415EEC418D38EC:0\r\n";

        HibpBreachedPasswordChecker.IsSuffixListed(response, "0018A45C4D1DEF81644B54AB7F969B88D65").Should().BeTrue();
        HibpBreachedPasswordChecker.IsSuffixListed(response, "00D4F6E8FA6EECAD2A3AA415EEC418D38EC").Should().BeFalse("padding entries have count 0");
        HibpBreachedPasswordChecker.IsSuffixListed(response, "FFFFF").Should().BeFalse();
    }

    // ---------- TOTP ----------

    [Fact]
    public void Totp_MatchesRfc6238Vector_AndToleratesOneStepDrift()
    {
        // RFC 6238 appendix B, SHA-1 secret "12345678901234567890", T=59s -> 94287082 (last 6 digits 287082).
        var key = Encoding.ASCII.GetBytes("12345678901234567890");
        TotpService.Compute(key, 59 / 30).Should().Be("287082");

        var secret = TotpService.ToBase32(key);
        var at = DateTimeOffset.FromUnixTimeSeconds(59).UtcDateTime;
        _totp.VerifyCode(secret, "287082", at).Should().BeTrue();
        _totp.VerifyCode(secret, "287082", at.AddSeconds(30)).Should().BeTrue();
        _totp.VerifyCode(secret, "287082", at.AddSeconds(120)).Should().BeFalse();
        _totp.VerifyCode(secret, "12ab56", at).Should().BeFalse();
    }

    // ---------- Throttling ----------

    [Fact]
    public void ProgressiveThrottle_NeverPermanent_CappedAt15Minutes()
    {
        var user = User.CreateVenueOwner(Guid.NewGuid(), _email, "+201001112222", "hash", "Owner", _clock.UtcNow);

        for (var i = 0; i < 4; i++)
        {
            user.RegisterFailedLogin(_clock.UtcNow);
        }

        user.IsLoginThrottled(_clock.UtcNow).Should().BeFalse();
        user.RegisterFailedLogin(_clock.UtcNow);
        user.LoginThrottledUntilUtc.Should().Be(_clock.UtcNow.AddMinutes(1));

        for (var i = 0; i < 20; i++)
        {
            user.RegisterFailedLogin(_clock.UtcNow);
        }

        user.LoginThrottledUntilUtc.Should().Be(_clock.UtcNow.Add(User.MaxThrottle));
        user.IsLoginThrottled(_clock.UtcNow.AddMinutes(16)).Should().BeFalse();
        user.RegisterSuccessfulLogin(_clock.UtcNow);
        user.FailedLoginCount.Should().Be(0);
    }

    // ---------- Flows ----------

    [Fact]
    public async Task Register_Verify_Login_HappyPathAsync()
    {
        await using var db = NewDb();
        var accounts = Accounts(db);

        (await accounts.HandleAsync(new RegisterVenueOwnerCommand(_email, "01001112222", "Owner", _password))).IsSuccess.Should().BeTrue();
        var stored = await db.Users.SingleAsync();
        stored.PasswordHash.Should().NotContain(_password);
        stored.Roles.Should().Contain(UserRole.VenueOwner);

        (await Login(db).HandleAsync(LoginCmd())).Error.Should().Be(PasswordAuthErrors.EmailNotVerified);

        _mailbox.Sent.Should().BeEmpty("the activation link goes to the phone, not the email");
        var token = _phone.LastToken();
        (await accounts.HandleAsync(new VerifyEmailCommand(token))).IsSuccess.Should().BeTrue();
        (await accounts.HandleAsync(new VerifyEmailCommand(token))).Error.Should().Be(PasswordAuthErrors.InvalidToken, "tokens are single-use");
        stored = await db.Users.SingleAsync();
        stored.IsPhoneNumberConfirmed.Should().BeTrue("the link proved the number");
        stored.IsEmailConfirmed.Should().BeFalse("nothing proved the email");

        var login = await Login(db).HandleAsync(LoginCmd());
        login.IsSuccess.Should().BeTrue();
        login.Value.AccessToken.Should().NotBeNullOrEmpty();
        (await db.RefreshTokens.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Login_UnknownEmailAndWrongPassword_SameGenericErrorAsync()
    {
        await using var db = NewDb();
        await SeedVerifiedOwnerAsync(db);

        var unknown = await Login(db).HandleAsync(LoginCmd() with { Email = "nobody@club.com" });
        var wrong = await Login(db).HandleAsync(LoginCmd() with { Password = "not the password" });

        unknown.Error.Should().Be(PasswordAuthErrors.InvalidCredentials);
        wrong.Error.Should().Be(PasswordAuthErrors.InvalidCredentials);
    }

    [Fact]
    public async Task Login_PlayerAccountWithPassword_IsRejectedAsync()
    {
        await using var db = NewDb();
        var player = User.CreateStaff(Guid.NewGuid(), "player@x.com", "+201001112299", _hasher.Hash(_password), "P", UserRole.Player, _clock.UtcNow);
        db.Users.Add(player);
        await db.SaveChangesAsync();

        (await Login(db).HandleAsync(LoginCmd() with { Email = "player@x.com" })).Error.Should().Be(PasswordAuthErrors.InvalidCredentials);
    }

    [Fact]
    public async Task Login_AfterFailures_RequiresCaptcha_ThenThrottlesAsync()
    {
        await using var db = NewDb();
        await SeedVerifiedOwnerAsync(db);
        var login = Login(db);

        for (var i = 0; i < User.CaptchaAfterFailures; i++)
        {
            await login.HandleAsync(LoginCmd() with { Password = "wrong wrong wrong" });
        }

        _turnstile.Allow = false;
        (await login.HandleAsync(LoginCmd())).Error.Should().Be(PasswordAuthErrors.CaptchaRequired);

        _turnstile.Allow = true;
        await login.HandleAsync(LoginCmd() with { Password = "wrong wrong wrong" });
        await login.HandleAsync(LoginCmd() with { Password = "wrong wrong wrong" });
        (await login.HandleAsync(LoginCmd())).Error.Should().Be(PasswordAuthErrors.Throttled, "even the right password waits out the delay");

        _clock.UtcNow = _clock.UtcNow.AddMinutes(2);
        (await login.HandleAsync(LoginCmd())).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ForgotAndReset_RevokesSessions_TokenSingleUse_UnknownEmailSilentAsync()
    {
        await using var db = NewDb();
        var user = await SeedVerifiedOwnerAsync(db);
        await Login(db).HandleAsync(LoginCmd());
        var accounts = Accounts(db);

        (await accounts.HandleAsync(new ForgotPasswordCommand("nobody@club.com"))).IsSuccess.Should().BeTrue();
        _mailbox.Sent.Should().BeEmpty();

        await accounts.HandleAsync(new ForgotPasswordCommand(_email));
        var token = _mailbox.LastToken();
        const string newPassword = "a brand new long password";

        (await accounts.HandleAsync(new ResetPasswordCommand(token, newPassword))).IsSuccess.Should().BeTrue();
        (await accounts.HandleAsync(new ResetPasswordCommand(token, "another long password"))).Error.Should().Be(PasswordAuthErrors.InvalidToken);
        (await db.RefreshTokens.AllAsync(t => t.IsRevoked)).Should().BeTrue();
        (await Login(db).HandleAsync(LoginCmd() with { Password = newPassword })).IsSuccess.Should().BeTrue();
        user.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Reset_ForAnUnverifiedEmail_ConfirmsIt_SoTheNewPasswordSignsInAtOnceAsync()
    {
        await using var db = NewDb();
        var accounts = Accounts(db);
        await accounts.HandleAsync(new RegisterVenueOwnerCommand(_email, "01001112222", "Owner", _password));
        (await Login(db).HandleAsync(LoginCmd())).Error.Should().Be(PasswordAuthErrors.EmailNotVerified);

        await accounts.HandleAsync(new ForgotPasswordCommand(_email));
        const string fresh = "a brand new long phrase";
        (await accounts.HandleAsync(new ResetPasswordCommand(_mailbox.LastToken(), fresh))).IsSuccess.Should().BeTrue();

        (await db.Users.SingleAsync()).IsEmailConfirmed.Should().BeTrue();
        (await Login(db).HandleAsync(LoginCmd() with { Password = fresh })).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Reset_ExpiredToken_FailsAsync()
    {
        await using var db = NewDb();
        await SeedVerifiedOwnerAsync(db);
        var accounts = Accounts(db);
        await accounts.HandleAsync(new ForgotPasswordCommand(_email));

        _clock.UtcNow = _clock.UtcNow.AddMinutes(31);

        (await accounts.HandleAsync(new ResetPasswordCommand(_mailbox.LastToken(), "a brand new long password"))).Error
            .Should().Be(PasswordAuthErrors.InvalidToken);
    }

    [Fact]
    public async Task ChangePassword_RequiresCurrentPasswordAsync()
    {
        await using var db = NewDb();
        var user = await SeedVerifiedOwnerAsync(db);
        var accounts = Accounts(db);

        (await accounts.HandleAsync(new ChangePasswordCommand(user.Id, "wrong current", "a brand new long password"))).Error
            .Should().Be(PasswordAuthErrors.InvalidCredentials);
        (await accounts.HandleAsync(new ChangePasswordCommand(user.Id, _password, "a brand new long password"))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Totp_Enroll_Confirm_ThenLoginRequiresCodeAsync()
    {
        await using var db = NewDb();
        var user = await SeedVerifiedOwnerAsync(db);
        var accounts = Accounts(db);

        var enrollment = await accounts.HandleAsync(new EnrollTotpCommand(user.Id));
        user.TotpSecretCiphertext.Should().NotBeNull();
        Encoding.UTF8.GetString(user.TotpSecretCiphertext!).Should().NotContain(enrollment.Value.Secret);

        var code = TotpService.Compute(TotpService.FromBase32(enrollment.Value.Secret), new DateTimeOffset(_clock.UtcNow).ToUnixTimeSeconds() / 30);
        (await accounts.HandleAsync(new ConfirmTotpCommand(user.Id, "000000"))).Error.Should().Be(PasswordAuthErrors.InvalidTotpCode);
        (await accounts.HandleAsync(new ConfirmTotpCommand(user.Id, code))).IsSuccess.Should().BeTrue();

        (await Login(db).HandleAsync(LoginCmd())).Error.Should().Be(PasswordAuthErrors.TwoFactorRequired);
        (await Login(db).HandleAsync(LoginCmd() with { TotpCode = code })).IsSuccess.Should().BeTrue();
    }

    // ---------- helpers ----------

    private static PasswordLoginCommand LoginCmd() => new(_email, _password, null, "token", null);

    private async Task<User> SeedVerifiedOwnerAsync(IdentityDbContext db)
    {
        var user = User.CreateVenueOwner(Guid.NewGuid(), _email, "+201001112222", _hasher.Hash(_password), "Owner", _clock.UtcNow);
        user.ConfirmEmail(_clock.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private PasswordLoginHandler Login(IdentityDbContext db) =>
        new(db, _hasher, new PhoneNumberNormalizer(), _turnstile, _totp, _cipher, NewTokenService(), TestSessions.Create(db, _clock), _clock, NullLogger<PasswordLoginHandler>.Instance);

    private PasswordAccountHandlers Accounts(IdentityDbContext db) =>
        new(db, _hasher, new PasswordPolicy(new FakeBreachChecker(false)), new PhoneNumberNormalizer(), _mailbox, _phone, _totp, _cipher, _clock,
            new ConfigurationBuilder().Build(), NullLogger<PasswordAccountHandlers>.Instance);

    private TokenService NewTokenService()
    {
        var options = Options.Create(new JwtOptions { CurrentKeyId = "test-key" });
        return new TokenService(new JwtKeyProvider(options), _clock, options);
    }

    private static IdentityDbContext NewDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Login_WithThePhoneNumberInsteadOfTheEmail_SignsInAsync()
    {
        await using var db = NewDb();
        await SeedVerifiedOwnerAsync(db);

        (await Login(db).HandleAsync(LoginCmd() with { Email = "01001112222" })).IsSuccess.Should().BeTrue();
        (await Login(db).HandleAsync(LoginCmd() with { Email = "+201001112222" })).IsSuccess.Should().BeTrue();
        (await Login(db).HandleAsync(LoginCmd() with { Email = "01009999999" })).Error.Should().Be(PasswordAuthErrors.InvalidCredentials);
        (await Login(db).HandleAsync(LoginCmd() with { Email = "not a number" })).Error.Should().Be(PasswordAuthErrors.InvalidCredentials);
    }

    [Fact]
    public async Task ResendActivation_GoesToTheAccountsPhone_AndStopsOnceActivatedAsync()
    {
        await using var db = NewDb();
        var accounts = Accounts(db);
        await accounts.HandleAsync(new RegisterVenueOwnerCommand(_email, "01001112222", "Owner", _password));
        _phone.Sent.Should().ContainSingle().Which.Phone.Should().Be("+201001112222");

        _clock.UtcNow = _clock.UtcNow.AddMinutes(2);
        await accounts.HandleAsync(new ResendVerificationCommand(_email));
        _phone.Sent.Should().HaveCount(2);

        await accounts.HandleAsync(new VerifyEmailCommand(_phone.LastToken()));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(2);
        await accounts.HandleAsync(new ResendVerificationCommand(_email));
        _phone.Sent.Should().HaveCount(2, "an activated account gets no more links");
        _mailbox.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task InvitedStaffAccount_IsStaffNotOwner_SignsInByPhone_AndRefusesADuplicateNumberAsync()
    {
        await using var db = NewDb();
        var contract = new UserAccountsContract(db, new PhoneNumberNormalizer(), _mailbox, _phone, _hasher,
            new PasswordPolicy(new FakeBreachChecker(false)), _clock, new ConfigurationBuilder().Build(), NullLogger<UserAccountsContract>.Instance);
        var account = new Malabak.SharedKernel.Contracts.InvitedStaffAccount("Staff", "+201001112299", null, _password, PhoneVerified: true, EmailVerified: false);

        var created = await contract.CreateInvitedStaffAccountAsync(account);

        var user = await db.Users.SingleAsync(u => u.Id == created.Value);
        user.Roles.Should().Equal(UserRole.VenueStaff);
        user.Email.Should().BeNull();
        user.IsPhoneNumberConfirmed.Should().BeTrue();
        (await Login(db).HandleAsync(LoginCmd() with { Email = "01001112299" })).IsSuccess.Should().BeTrue();
        (await contract.CreateInvitedStaffAccountAsync(account with { FullName = "Other" })).Error.Should().Be(StaffAccountErrors.AlreadyRegistered);
        (await contract.CreateInvitedStaffAccountAsync(account with { NormalizedPhone = "+201001112288", Password = _tooShortPassphrase })).IsFailure.Should().BeTrue();
    }

    private sealed class RecordingPhoneAlerts : ISmsAlertService
    {
        public List<(string Phone, SecurityAlert Alert)> Sent { get; } = [];

        public Task SendSecurityAlertAsync(string phoneNumber, SecurityAlert alert, CancellationToken cancellationToken = default)
        {
            Sent.Add((phoneNumber, alert));
            return Task.CompletedTask;
        }

        public string LastToken()
        {
            var link = Sent[^1].Alert.ActivationLink!;
            return link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..];
        }
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<string> Sent { get; } = [];

        public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default)
        {
            Sent.Add(body);
            return Task.CompletedTask;
        }

        public string LastToken() => new(Sent[^1][(Sent[^1].IndexOf("token=", StringComparison.Ordinal) + 6)..].TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
    }

    private sealed class FakeBreachChecker(bool breached) : IBreachedPasswordChecker
    {
        public Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default) => Task.FromResult(breached);
    }

    private sealed class AllowTurnstile : ITurnstileValidator
    {
        public bool Allow { get; set; } = true;

        public Task<bool> ValidateTokenAsync(string? turnstileToken, string? remoteIp = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Allow);
    }

    private sealed class FakeCipher : IFieldCipher
    {
        public ValueTask<byte[]> EncryptAsync(string plaintext, FieldEncryptionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Encoding.UTF8.GetBytes(plaintext).Select(b => (byte)(b ^ 0x33)).ToArray());

        public ValueTask<string> DecryptAsync(byte[] ciphertext, FieldEncryptionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Encoding.UTF8.GetString(ciphertext.Select(b => (byte)(b ^ 0x33)).ToArray()));

        public ValueTask<bool> NeedsReEncryptionAsync(byte[] ciphertext, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class MutableClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
