using System.Text;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.StepUp;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

public sealed class StepUpTests
{
    private const string _password = "owner long passphrase";

    private readonly MutableClock _clock = new();
    private readonly Pbkdf2PasswordHasher _hasher = new();
    private readonly CapturingOtpSender _sms = new();

    private StepUpService Service() => new(Options.Create(new StepUpOptions { SecretKey = TestSecrets.StepUpSecretKey }), _clock);

    [Fact]
    public void Token_IsScopedToUserAndScope_AndExpiresAfter5Minutes()
    {
        var userId = Guid.NewGuid();
        var token = Service().IssueStepUpToken(userId, StepUpScopes.DeleteAccount, binding: "stamp-1");

        Service().ValidateStepUpToken(token, userId, StepUpScopes.DeleteAccount, binding: "stamp-1").Should().BeTrue();
        Service().ValidateStepUpToken(token, Guid.NewGuid(), StepUpScopes.DeleteAccount, binding: "stamp-1").Should().BeFalse();
        Service().ValidateStepUpToken(token, userId, StepUpScopes.ChangePhone, binding: "stamp-1").Should().BeFalse();

        _clock.UtcNow = _clock.UtcNow.AddMinutes(6);
        Service().ValidateStepUpToken(token, userId, StepUpScopes.DeleteAccount, binding: "stamp-1").Should().BeFalse();
    }

    [Fact]
    public void Token_BoundToSecurityStamp_DiesWhenStampChanges_AndTamperingFails()
    {
        var userId = Guid.NewGuid();
        var token = Service().IssueStepUpToken(userId, StepUpScopes.ChangePhone, binding: "stamp-1");

        Service().ValidateStepUpToken(token, userId, StepUpScopes.ChangePhone, binding: "stamp-2").Should().BeFalse();

        var parts = token.Split(':');
        parts[1] = StepUpScopes.AdminDestructive;
        Service().ValidateStepUpToken(string.Join(':', parts), userId, StepUpScopes.AdminDestructive).Should().BeFalse();
    }

    [Fact]
    public async Task Password_StepUp_IssuesBoundToken_ThatVerifierAcceptsUntilPasswordChangesAsync()
    {
        await using var db = NewDb();
        var user = await SeedOwnerAsync(db);
        var handlers = Handlers(db);

        var result = await handlers.HandleAsync(new VerifyStepUpCommand(user.Id, StepUpScopes.DeleteAccount, StepUpMethod.Password, _password));

        result.Value.ExpiresInSeconds.Should().Be(300);
        (await handlers.IsValidAsync(user.Id, StepUpScopes.DeleteAccount, result.Value.StepUpToken)).Should().BeTrue();
        (await handlers.IsValidAsync(user.Id, StepUpScopes.ChangePhone, result.Value.StepUpToken)).Should().BeFalse();

        user.SetPassword(_hasher.Hash("a completely new passphrase"), _clock.UtcNow);
        await db.SaveChangesAsync();
        (await handlers.IsValidAsync(user.Id, StepUpScopes.DeleteAccount, result.Value.StepUpToken)).Should().BeFalse();
    }

    [Fact]
    public async Task WrongPassword_Fails_AndCountsTowardsThrottleAsync()
    {
        await using var db = NewDb();
        var user = await SeedOwnerAsync(db);

        var result = await Handlers(db).HandleAsync(new VerifyStepUpCommand(user.Id, StepUpScopes.DeleteAccount, StepUpMethod.Password, "nope nope nope"));

        result.Error.Should().Be(StepUpErrors.VerificationFailed);
        user.FailedLoginCount.Should().Be(1);
    }

    [Fact]
    public async Task UnknownScope_And_UnavailableMethod_AreRejectedAsync()
    {
        await using var db = NewDb();
        var user = await SeedOwnerAsync(db);
        var handlers = Handlers(db);

        (await handlers.HandleAsync(new VerifyStepUpCommand(user.Id, "make_me_admin", StepUpMethod.Password, _password))).Error
            .Should().Be(StepUpErrors.UnknownScope);
        (await handlers.HandleAsync(new VerifyStepUpCommand(user.Id, StepUpScopes.DeleteAccount, StepUpMethod.Totp, "123456"))).Error
            .Should().Be(StepUpErrors.MethodNotAvailable, "TOTP isn't enabled for this user");
        (await handlers.HandleAsync(new VerifyStepUpCommand(user.Id, StepUpScopes.AdminDestructive, StepUpMethod.Otp, "123456"))).Error
            .Should().Be(StepUpErrors.MethodNotAvailable, "admin-destructive never accepts an SMS code");
        (await handlers.HandleAsync(new VerifyStepUpCommand(user.Id, StepUpScopes.AdminDestructive, StepUpMethod.Password, _password))).IsSuccess
            .Should().BeTrue("without an authenticator the password is the strongest factor the account has");
        user.SetPendingTotpSecret([1, 2, 3], DateTime.UtcNow);
        user.SetTwoFactorEnabled(true, DateTime.UtcNow);
        await db.SaveChangesAsync();
        (await handlers.HandleAsync(new VerifyStepUpCommand(user.Id, StepUpScopes.AdminDestructive, StepUpMethod.Password, _password))).Error
            .Should().Be(StepUpErrors.MethodNotAvailable, "once the authenticator is on, admin-destructive requires it");
    }

    [Fact]
    public async Task Otp_StepUp_SendsCodeToCurrentPhone_CodeSingleUseAsync()
    {
        await using var db = NewDb();
        var user = await SeedOwnerAsync(db);
        var handlers = Handlers(db);

        (await handlers.HandleAsync(new SendStepUpOtpCommand(user.Id))).IsSuccess.Should().BeTrue();
        _sms.LastPhone.Should().Be(user.PhoneNumber);
        _sms.LastPurpose.Should().Be(OtpPurpose.StepUp);
        var code = _sms.LastCode();

        (await handlers.HandleAsync(new VerifyStepUpCommand(user.Id, StepUpScopes.ChangePhone, StepUpMethod.Otp, code))).IsSuccess.Should().BeTrue();
        (await handlers.HandleAsync(new VerifyStepUpCommand(user.Id, StepUpScopes.ChangePhone, StepUpMethod.Otp, code))).Error
            .Should().Be(StepUpErrors.VerificationFailed);
    }

    [Fact]
    public async Task Verifier_RejectsMissingOrForeignTokensAsync()
    {
        await using var db = NewDb();
        var user = await SeedOwnerAsync(db);
        var handlers = Handlers(db);

        (await handlers.IsValidAsync(user.Id, StepUpScopes.DeleteAccount, null)).Should().BeFalse();
        var unbound = Service().IssueStepUpToken(user.Id, StepUpScopes.DeleteAccount, binding: "other-stamp");
        (await handlers.IsValidAsync(user.Id, StepUpScopes.DeleteAccount, unbound)).Should().BeFalse();
    }

    private async Task<User> SeedOwnerAsync(IdentityDbContext db)
    {
        var user = User.CreateVenueOwner(Guid.NewGuid(), "owner@club.com", "+201001112222", _hasher.Hash(_password), "Owner", _clock.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private StepUpHandlers Handlers(IdentityDbContext db) => new(
        db, Service(), _hasher, new TotpService(), new NoCipher(), new InMemoryOtpStore(), new OtpSecurityService(Options.Create(new OtpSecurityOptions { HmacSecret = TestSecrets.OtpHmacSecret })), _sms, _clock,
        NullLogger<StepUpHandlers>.Instance);

    private static IdentityDbContext NewDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class CapturingOtpSender : IOtpSender
    {
        public string? LastPhone { get; private set; }
        public OtpPurpose? LastPurpose { get; private set; }
        private string _lastCode = string.Empty;

        public Task<bool> SendAsync(string phoneNumber, string code, OtpPurpose purpose, TimeSpan validity, CancellationToken cancellationToken = default)
        {
            LastPhone = phoneNumber;
            LastPurpose = purpose;
            _lastCode = code;
            return Task.FromResult(true);
        }

        public string LastCode() => _lastCode;
    }

    private sealed class NoCipher : IFieldCipher
    {
        public ValueTask<byte[]> EncryptAsync(string plaintext, FieldEncryptionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Encoding.UTF8.GetBytes(plaintext));

        public ValueTask<string> DecryptAsync(byte[] ciphertext, FieldEncryptionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Encoding.UTF8.GetString(ciphertext));

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
