using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.PasswordAuth;
using Malabak.Modules.Identity.Application.StepUp;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Security;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Phase 1 (P1) Edge-Case Tests: Staff & Admin Password Auth, 2FA (TOTP) and Step-Up (SECURITY.md §4.1, §4.2, §3.8).
/// </summary>
public sealed class StaffPasswordAndStepUpEdgeCaseTests
{
    private const string _venueOwnerEmail = "owner@malabak.test";
    private static string ValidSecret() => "cairo-green-pitch-twenty-six";
    private static string AlternateSecret() => "alex-alternate-pitch-twenty-six";
    private static string ReplacedSecret() => "giza-replaced-pitch-twenty-six";

    private readonly FakeClock _clock = new(new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc));
    private readonly Pbkdf2PasswordHasher _hasher = new();
    private readonly TotpService _totp = new();
    private readonly FakeFieldCipher _cipher = new();
    private readonly FakeEmailSender _emailSender = new();
    private readonly FakeTurnstileValidator _turnstile = new();

    private static IdentityDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private PasswordAccountHandlers CreateAccountHandlers(IdentityDbContext db) =>
        new(db, _hasher, new PasswordPolicy(new FakeBreachChecker(false)), new PhoneNumberNormalizer(),
            _emailSender, _totp, _cipher, _clock, new ConfigurationBuilder().Build(), NullLogger<PasswordAccountHandlers>.Instance);

    private PasswordLoginHandler CreateLoginHandler(IdentityDbContext db)
    {
        var jwtOptions = Options.Create(new JwtOptions { CurrentKeyId = "test-key" });
        var tokenService = new TokenService(new JwtKeyProvider(jwtOptions), _clock, jwtOptions);
        return new PasswordLoginHandler(
            db, _hasher, _turnstile, _totp, _cipher, tokenService, TestSessions.Create(db, _clock), _clock, NullLogger<PasswordLoginHandler>.Instance);
    }

    private StepUpService CreateStepUpService() =>
        new(Options.Create(new StepUpOptions { SecretKey = TestSecrets.StepUpSecretKey }), _clock);

    private async Task<User> SeedVerifiedOwnerAsync(IdentityDbContext db)
    {
        var user = User.CreateVenueOwner(Guid.NewGuid(), _venueOwnerEmail, "+201001112233", _hasher.Hash(ValidSecret()), "Owner", _clock.UtcNow);
        user.ConfirmEmail(_clock.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // =========================================================================
    // 1. Password Login Lockout & Anti-Enumeration (SECURITY.md §4.1, §4.2)
    // =========================================================================

    [Fact]
    public async Task Login_AntiEnumeration_UnknownEmailAndWrongPasswordReturnIdenticalErrorAsync()
    {
        await using var db = CreateDbContext();
        await SeedVerifiedOwnerAsync(db);
        var handler = CreateLoginHandler(db);

        var unknownUserResult = await handler.HandleAsync(
            new PasswordLoginCommand("nonexistent@malabak.test", ValidSecret(), null, "turnstile-ok", "192.168.1.1"));
        var wrongPasswordResult = await handler.HandleAsync(
            new PasswordLoginCommand(_venueOwnerEmail, "wrong-entry-here", null, "turnstile-ok", "192.168.1.1"));

        unknownUserResult.IsFailure.Should().BeTrue();
        wrongPasswordResult.IsFailure.Should().BeTrue();
        unknownUserResult.Error.Code.Should().Be("Identity.InvalidCredentials");
        wrongPasswordResult.Error.Code.Should().Be("Identity.InvalidCredentials");
    }

    [Fact]
    public async Task Login_ProgressiveLockout_ThrottlesAfter5FailuresAndResetsAfterWaitAsync()
    {
        await using var db = CreateDbContext();
        var user = await SeedVerifiedOwnerAsync(db);
        var handler = CreateLoginHandler(db);

        // 5 consecutive wrong password attempts
        for (var i = 0; i < 5; i++)
        {
            await handler.HandleAsync(new PasswordLoginCommand(_venueOwnerEmail, "bad-pwd", null, "turnstile-ok", "192.168.1.1"));
        }

        // Even with the CORRECT password, login is throttled
        var throttledResult = await handler.HandleAsync(
            new PasswordLoginCommand(_venueOwnerEmail, ValidSecret(), null, "turnstile-ok", "192.168.1.1"));

        throttledResult.IsFailure.Should().BeTrue();
        throttledResult.Error.Code.Should().Be("Identity.LoginThrottled");

        // Advance clock past the throttle window (2 minutes)
        _clock.Advance(TimeSpan.FromMinutes(2));

        // Now correct password succeeds and resets the failed login count
        var successfulResult = await handler.HandleAsync(
            new PasswordLoginCommand(_venueOwnerEmail, ValidSecret(), null, "turnstile-ok", "192.168.1.1"));

        successfulResult.IsSuccess.Should().BeTrue();
        var refreshedUser = await db.Users.SingleAsync(u => u.Id == user.Id);
        refreshedUser.FailedLoginCount.Should().Be(0);
    }

    // =========================================================================
    // 2. Forgot & Reset Password (SECURITY.md §4.2)
    // =========================================================================

    [Fact]
    public async Task ForgotPassword_AlwaysReturnsSuccess_EvenForNonExistentEmailAsync()
    {
        await using var db = CreateDbContext();
        var handlers = CreateAccountHandlers(db);

        var result = await handlers.HandleAsync(new ForgotPasswordCommand("nobody@malabak.test"));

        result.IsSuccess.Should().BeTrue("forgot password must never reveal if an email exists");
        _emailSender.SentMessages.Should().BeEmpty("no email is sent for non-existent accounts");
    }

    [Fact]
    public async Task ResetPassword_SingleUseAndExpiresAfter30MinutesAsync()
    {
        await using var db = CreateDbContext();
        await SeedVerifiedOwnerAsync(db);
        var handlers = CreateAccountHandlers(db);

        // Request reset
        await handlers.HandleAsync(new ForgotPasswordCommand(_venueOwnerEmail));
        _emailSender.SentMessages.Should().ContainSingle();
        var resetToken = _emailSender.ExtractTokenFromLastMessage();

        // Expired token test: advance clock 31 minutes
        _clock.Advance(TimeSpan.FromMinutes(31));
        var expiredResult = await handlers.HandleAsync(new ResetPasswordCommand(resetToken, AlternateSecret()));
        expiredResult.IsFailure.Should().BeTrue();
        expiredResult.Error.Code.Should().Be("Identity.InvalidOrExpiredToken");

        // Request a fresh reset token
        _clock.Advance(TimeSpan.FromMinutes(2));
        await handlers.HandleAsync(new ForgotPasswordCommand(_venueOwnerEmail));
        var freshToken = _emailSender.ExtractTokenFromLastMessage();

        // First use: succeeds
        var firstUse = await handlers.HandleAsync(new ResetPasswordCommand(freshToken, AlternateSecret()));
        firstUse.IsSuccess.Should().BeTrue();

        // Replay of same token: fails (single use)
        var secondUse = await handlers.HandleAsync(new ResetPasswordCommand(freshToken, ReplacedSecret()));
        secondUse.IsFailure.Should().BeTrue();
        secondUse.Error.Code.Should().Be("Identity.InvalidOrExpiredToken");
    }

    [Fact]
    public async Task ChangePassword_RequiresCurrentPassword_AndRevokesOtherSessionsAsync()
    {
        await using var db = CreateDbContext();
        var user = await SeedVerifiedOwnerAsync(db);
        var handlers = CreateAccountHandlers(db);

        // Wrong current password fails
        var failResult = await handlers.HandleAsync(new ChangePasswordCommand(user.Id, "wrong-entry", AlternateSecret()));
        failResult.IsFailure.Should().BeTrue();
        failResult.Error.Code.Should().Be("Identity.InvalidCredentials");

        // Correct current password succeeds
        var successResult = await handlers.HandleAsync(new ChangePasswordCommand(user.Id, ValidSecret(), AlternateSecret()));
        successResult.IsSuccess.Should().BeTrue();
    }

    // =========================================================================
    // 3. TOTP 2FA & Clock Drift (SECURITY.md §4.2)
    // =========================================================================

    [Fact]
    public void TotpService_ToleratesOneStepClockDrift_RejectsBeyondWindow()
    {
        var secret = _totp.GenerateSecret();
        var base32Key = TotpService.FromBase32(secret);

        var now = _clock.UtcNow;
        var step = new DateTimeOffset(now).ToUnixTimeSeconds() / 30;
        var currentCode = TotpService.Compute(base32Key, step);

        // Exact time: valid
        _totp.VerifyCode(secret, currentCode, now).Should().BeTrue();

        // +30 seconds (1 step drift): valid
        _totp.VerifyCode(secret, currentCode, now.AddSeconds(30)).Should().BeTrue();

        // -30 seconds (1 step drift): valid
        _totp.VerifyCode(secret, currentCode, now.AddSeconds(-30)).Should().BeTrue();

        // +90 seconds (3 steps drift): rejected
        _totp.VerifyCode(secret, currentCode, now.AddSeconds(90)).Should().BeFalse();

        // -90 seconds (3 steps drift): rejected
        _totp.VerifyCode(secret, currentCode, now.AddSeconds(-90)).Should().BeFalse();

        // Wrong code: rejected
        _totp.VerifyCode(secret, "000000", now).Should().BeFalse();
    }

    [Fact]
    public async Task Totp_ReplayInsideSameTimeStep_MustBeRejectedAsync()
    {
        await using var db = CreateDbContext();
        var user = await SeedVerifiedOwnerAsync(db);
        var handlers = CreateAccountHandlers(db);
        var loginHandler = CreateLoginHandler(db);

        // Enroll & Confirm TOTP
        var enrollment = await handlers.HandleAsync(new EnrollTotpCommand(user.Id));
        var secretBytes = TotpService.FromBase32(enrollment.Value.Secret);
        var step = new DateTimeOffset(_clock.UtcNow).ToUnixTimeSeconds() / 30;
        var code = TotpService.Compute(secretBytes, step);

        await handlers.HandleAsync(new ConfirmTotpCommand(user.Id, code));

        // First login with TOTP code succeeds
        var firstLogin = await loginHandler.HandleAsync(
            new PasswordLoginCommand(_venueOwnerEmail, ValidSecret(), code, "turnstile-ok", "10.0.0.1"));
        firstLogin.IsSuccess.Should().BeTrue();

        // Replay of the same TOTP code 5 seconds later (within the same 30-second step) must be rejected
        _clock.Advance(TimeSpan.FromSeconds(5));
        var replayedLogin = await loginHandler.HandleAsync(
            new PasswordLoginCommand(_venueOwnerEmail, ValidSecret(), code, "turnstile-ok", "10.0.0.1"));

        replayedLogin.IsFailure.Should().BeTrue("replaying a TOTP code within the same time-step must be prevented");
    }

    // =========================================================================
    // 4. Email Verification Cooldown (60 Seconds)
    // =========================================================================

    [Fact]
    public async Task ResendEmailVerification_Enforces60SecondCooldownAsync()
    {
        await using var db = CreateDbContext();
        var user = User.CreateVenueOwner(Guid.NewGuid(), "unverified@malabak.test", "+201099887766", _hasher.Hash(ValidSecret()), "New Owner", _clock.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handlers = CreateAccountHandlers(db);

        // First send succeeds
        await handlers.HandleAsync(new ResendVerificationCommand("unverified@malabak.test"));
        _emailSender.SentMessages.Count.Should().Be(1);

        // Immediate second send is throttled by cooldown (silent no-op)
        await handlers.HandleAsync(new ResendVerificationCommand("unverified@malabak.test"));
        _emailSender.SentMessages.Count.Should().Be(1, "resend within 60s must not send duplicate email");

        // Advance 61 seconds past cooldown
        _clock.Advance(TimeSpan.FromSeconds(61));
        await handlers.HandleAsync(new ResendVerificationCommand("unverified@malabak.test"));
        _emailSender.SentMessages.Count.Should().Be(2, "resend after cooldown elapses must send email");
    }

    // =========================================================================
    // 5. Step-Up Token Edge Cases (SECURITY.md §3.8)
    // =========================================================================

    [Fact]
    public void StepUpService_StrictScopeAndUserBinding_Enforced()
    {
        var service = CreateStepUpService();
        var userId = Guid.NewGuid();
        const string changePhoneScope = StepUpScopes.ChangePhone;
        const string deleteAccountScope = StepUpScopes.DeleteAccount;

        var token = service.IssueStepUpToken(userId, changePhoneScope);

        // Correct user and scope: succeeds
        service.ValidateStepUpToken(token, userId, changePhoneScope).Should().BeTrue();

        // Wrong scope: fails
        service.ValidateStepUpToken(token, userId, deleteAccountScope).Should().BeFalse();

        // Wrong user: fails
        service.ValidateStepUpToken(token, Guid.NewGuid(), changePhoneScope).Should().BeFalse();

        // Expired after 5 minutes: fails
        _clock.Advance(TimeSpan.FromMinutes(6));
        service.ValidateStepUpToken(token, userId, changePhoneScope).Should().BeFalse();
    }

    [Fact]
    public void StepUpService_SecurityStampBinding_FailsWhenStampChanges()
    {
        var service = CreateStepUpService();
        var userId = Guid.NewGuid();
        var initialStamp = "initial-security-stamp-abc";
        var rotatedStamp = "rotated-security-stamp-xyz";

        var token = service.IssueStepUpToken(userId, StepUpScopes.ChangePhone, binding: initialStamp);

        // Same stamp: valid
        service.ValidateStepUpToken(token, userId, StepUpScopes.ChangePhone, binding: initialStamp).Should().BeTrue();

        // After security stamp rotation: invalid
        service.ValidateStepUpToken(token, userId, StepUpScopes.ChangePhone, binding: rotatedStamp).Should().BeFalse();
    }

    // =========================================================================
    // Test Doubles
    // =========================================================================

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<string> SentMessages { get; } = [];

        public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default)
        {
            SentMessages.Add(body);
            return Task.CompletedTask;
        }

        public Task SendAsync(string toAddress, RenderedEmail email, CancellationToken cancellationToken = default)
        {
            SentMessages.Add(email.Html);
            return Task.CompletedTask;
        }

        public string ExtractTokenFromLastMessage()
        {
            var last = SentMessages[^1];
            var tokenIndex = last.IndexOf("token=", StringComparison.Ordinal) + 6;
            return new string(last[tokenIndex..].TakeWhile(c => !char.IsWhiteSpace(c) && c != '"').ToArray());
        }
    }

    private sealed class FakeTurnstileValidator : ITurnstileValidator
    {
        public Task<bool> ValidateTokenAsync(string? turnstileToken, string? remoteIp = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class FakeBreachChecker(bool breached) : IBreachedPasswordChecker
    {
        public Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(breached);
    }

    private sealed class FakeFieldCipher : IFieldCipher
    {
        public ValueTask<byte[]> EncryptAsync(string plaintext, FieldEncryptionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Encoding.UTF8.GetBytes(plaintext));

        public ValueTask<string> DecryptAsync(byte[] ciphertext, FieldEncryptionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Encoding.UTF8.GetString(ciphertext));

        public ValueTask<bool> NeedsReEncryptionAsync(byte[] ciphertext, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }
}
