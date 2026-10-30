using System.Text.Json;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.PhoneChange.RecoverPhone;
using Malabak.Modules.Identity.Application.PhoneChange.StartPhoneChange;
using Malabak.Modules.Identity.Application.PhoneChange.VerifyNewPhone;
using Malabak.Modules.Identity.Application.PhoneChange.VerifyOldPhone;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

public sealed class PhoneChangeTests
{
    private readonly MutableClock _clock = new(new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc));
    private readonly IPhoneNumberNormalizer _normalizer = new PhoneNumberNormalizer();
    private readonly IOtpSecurityService _otpSecurityService;
    private readonly ITokenService _tokenService;
    private readonly IStepUpService _stepUpService;
    private readonly RecordingSmsAlertService _smsAlertService = new();
    private readonly RecordingOtpSender _otpSender = new();

    public PhoneChangeTests()
    {
        _otpSecurityService = new OtpSecurityService(Options.Create(new OtpSecurityOptions { HmacSecret = TestSecrets.OtpHmacSecret }));
        _stepUpService = new StepUpService(Options.Create(new StepUpOptions { SecretKey = TestSecrets.StepUpSecretKey }), _clock);

        var jwtOptions = new JwtOptions { CurrentKeyId = "test-key-v1" };
        var keyProvider = new JwtKeyProvider(Options.Create(jwtOptions));
        _tokenService = new TokenService(keyProvider, _clock, Options.Create(jwtOptions));
    }

    private static IdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new IdentityDbContext(options);
    }

    [Fact]
    public void StepUpService_ShouldIssueAndValidateValidTokenWithin5Minutes()
    {
        var userId = Guid.NewGuid();
        var token = _stepUpService.IssueStepUpToken(userId, "change_phone");

        var isValid = _stepUpService.ValidateStepUpToken(token, userId, "change_phone", TimeSpan.FromMinutes(5));
        isValid.Should().BeTrue();

        // Wrong user
        _stepUpService.ValidateStepUpToken(token, Guid.NewGuid(), "change_phone").Should().BeFalse();

        // Wrong scope
        _stepUpService.ValidateStepUpToken(token, userId, "delete_account").Should().BeFalse();

        // Expired after 6 minutes
        _clock.Advance(TimeSpan.FromMinutes(6));
        _stepUpService.ValidateStepUpToken(token, userId, "change_phone", TimeSpan.FromMinutes(5)).Should().BeFalse();
    }

    [Fact]
    public async Task StartPhoneChange_WhenCooldownActive_ShouldRejectAsync()
    {
        using var dbContext = CreateDbContext();
        var handler = new StartPhoneChangeHandler(
            dbContext, _stepUpService, _normalizer, _otpSecurityService, _otpSender, _clock, NullLogger<StartPhoneChangeHandler>.Instance);

        var user = User.CreatePlayer(Guid.NewGuid(), "+201011112222", "Test Player", _clock.UtcNow);
        // Phone was changed 3 days ago
        user.ChangePhoneNumber("+201011112222", _clock.UtcNow.AddDays(-3));
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var stepUpToken = _stepUpService.IssueStepUpToken(user.Id, "change_phone");
        var command = new StartPhoneChangeCommand(user.Id, "01099998888", stepUpToken);

        var result = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.PhoneChangeCooldownActive");
    }

    [Fact]
    public async Task StartPhoneChange_WhenInvalidStepUpToken_ShouldRejectAsync()
    {
        using var dbContext = CreateDbContext();
        var handler = new StartPhoneChangeHandler(
            dbContext, _stepUpService, _normalizer, _otpSecurityService, _otpSender, _clock, NullLogger<StartPhoneChangeHandler>.Instance);

        var user = User.CreatePlayer(Guid.NewGuid(), "+201011112222", "Test Player", _clock.UtcNow);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var command = new StartPhoneChangeCommand(user.Id, "01099998888", "invalid:step:up:token");
        var result = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.InvalidStepUpToken");
    }

    [Fact]
    public async Task CompleteMultiStepPhoneChange_FullFlowSuccess_ShouldUpdateUserAndRevokeSessionsAsync()
    {
        using var dbContext = CreateDbContext();
        var startHandler = new StartPhoneChangeHandler(
            dbContext, _stepUpService, _normalizer, _otpSecurityService, _otpSender, _clock, NullLogger<StartPhoneChangeHandler>.Instance);
        var verifyOldHandler = new VerifyOldPhoneHandler(
            dbContext, _otpSecurityService, _otpSender, _clock, NullLogger<VerifyOldPhoneHandler>.Instance);
        var verifyNewLogger = new CapturingLogger<VerifyNewPhoneHandler>();
        var verifyNewHandler = new VerifyNewPhoneHandler(
            dbContext, _otpSecurityService, _tokenService, _smsAlertService, _clock, verifyNewLogger);

        // 1. Arrange user and active session
        var oldPhone = "+201011112222";
        var newPhoneRaw = "01099998888";
        var normalizedNewPhone = "+201099998888";

        var user = User.CreatePlayer(Guid.NewGuid(), oldPhone, "Amr", _clock.UtcNow);
        var initialSecurityStamp = user.SecurityStamp;
        dbContext.Users.Add(user);

        var activeToken = RefreshToken.Create(Guid.NewGuid(), user.Id, "token_hash_1", Guid.NewGuid(), _clock.UtcNow.AddDays(7), _clock.UtcNow);
        dbContext.RefreshTokens.Add(activeToken);
        await dbContext.SaveChangesAsync();

        // 2. Start Phone Change
        var stepUpToken = _stepUpService.IssueStepUpToken(user.Id, "change_phone");
        var startResult = await startHandler.HandleAsync(new StartPhoneChangeCommand(user.Id, newPhoneRaw, stepUpToken));

        startResult.IsSuccess.Should().BeTrue();
        var requestId = startResult.Value.RequestId;

        // Inspect OTP sent to old phone
        _otpSender.LastRecipient.Should().Be(oldPhone);
        _otpSender.LastPurpose.Should().Be(OtpPurpose.ChangePhoneOld);
        var oldOtp = _otpSender.LastCode;

        // 3. Verify Old Phone
        var verifyOldResult = await verifyOldHandler.HandleAsync(new VerifyOldPhoneCommand(user.Id, requestId, oldOtp));
        verifyOldResult.IsSuccess.Should().BeTrue();

        // Inspect OTP sent to new phone
        _otpSender.LastRecipient.Should().Be(normalizedNewPhone);
        _otpSender.LastPurpose.Should().Be(OtpPurpose.ChangePhoneNew);
        var newOtp = _otpSender.LastCode;

        // 4. Verify New Phone (Completes Change)
        var verifyNewResult = await verifyNewHandler.HandleAsync(new VerifyNewPhoneCommand(user.Id, requestId, newOtp));
        verifyNewResult.IsSuccess.Should().BeTrue();
        verifyNewResult.Value.NewPhoneNumber.Should().Be(normalizedNewPhone);

        // Verify DB updates
        var updatedUser = await dbContext.Users.FirstAsync(u => u.Id == user.Id);
        updatedUser.PhoneNumber.Should().Be(normalizedNewPhone);
        updatedUser.IsPhoneNumberConfirmed.Should().BeTrue();
        updatedUser.SecurityStamp.Should().NotBe(initialSecurityStamp);
        updatedUser.LastPhoneChangeAtUtc.Should().Be(_clock.UtcNow);

        // Verify active sessions revoked
        var tokenInDb = await dbContext.RefreshTokens.FirstAsync(t => t.Id == activeToken.Id);
        tokenInDb.IsRevoked.Should().BeTrue();

        // Verify alert sent to old phone with recovery link
        _smsAlertService.LastRecipient.Should().Be(oldPhone);
        _smsAlertService.LastAlert!.Kind.Should().Be(SecurityAlertKind.PhoneChanged);
        _smsAlertService.LastAlert.NewPhoneNumber.Should().Be(normalizedNewPhone);
        _smsAlertService.LastAlert.RecoveryLink.Should().StartWith("https://malabak.app/recover-phone?token=");
        var recoveryToken = _smsAlertService.LastAlert.RecoveryLink!["https://malabak.app/recover-phone?token=".Length..];
        recoveryToken.Should().NotBeNullOrWhiteSpace();

        // The token goes only to the old phone, never back to the session that made the change (SECURITY.md §5.5)
        JsonSerializer.Serialize(verifyNewResult.Value).Should().NotContain(recoveryToken);

        // Neither number nor the recovery token reaches the logs (SECURITY.md §15.5)
        verifyNewLogger.Entries.Should().NotBeEmpty();
        verifyNewLogger.AllText.Should().NotContain(oldPhone)
            .And.NotContain(normalizedNewPhone)
            .And.NotContain(recoveryToken);

        // The token from the alert restores the old number
        var recoverHandler = new RecoverPhoneHandler(
            dbContext, _tokenService, _smsAlertService, _clock, NullLogger<RecoverPhoneHandler>.Instance);
        var recoverResult = await recoverHandler.HandleAsync(new RecoverPhoneCommand(recoveryToken));
        recoverResult.IsSuccess.Should().BeTrue();
        (await dbContext.Users.FirstAsync(u => u.Id == user.Id)).PhoneNumber.Should().Be(oldPhone);
    }

    [Fact]
    public async Task RecoverPhone_Within24Hours_ShouldRevertPhoneNumberAndSecurityStampAsync()
    {
        using var dbContext = CreateDbContext();
        var recoverLogger = new CapturingLogger<RecoverPhoneHandler>();
        var recoverHandler = new RecoverPhoneHandler(
            dbContext, _tokenService, _smsAlertService, _clock, recoverLogger);

        var oldPhone = "+201011112222";
        var newPhone = "+201099998888";

        var user = User.CreatePlayer(Guid.NewGuid(), newPhone, "Karim", _clock.UtcNow);
        user.ChangePhoneNumber(newPhone, _clock.UtcNow);
        var postChangeSecurityStamp = user.SecurityStamp;
        dbContext.Users.Add(user);

        var recoveryTokenRaw = "my-secret-recovery-token-xyz";
        var recoveryTokenHash = _tokenService.HashRefreshToken(recoveryTokenRaw);

        var completedRequest = PhoneChangeRequest.Start(
            Guid.NewGuid(), user.Id, oldPhone, newPhone, "old_hash", _clock.UtcNow.AddMinutes(15), _clock.UtcNow.AddHours(-2));
        completedRequest.MarkOldPhoneVerified("new_hash", _clock.UtcNow.AddHours(-2));
        completedRequest.Complete(recoveryTokenHash, _clock.UtcNow.AddHours(-2));
        dbContext.PhoneChangeRequests.Add(completedRequest);

        await dbContext.SaveChangesAsync();

        // Recover within 24 hours
        var result = await recoverHandler.HandleAsync(new RecoverPhoneCommand(recoveryTokenRaw));

        result.IsSuccess.Should().BeTrue();

        var userInDb = await dbContext.Users.FirstAsync(u => u.Id == user.Id);
        userInDb.PhoneNumber.Should().Be(oldPhone);
        userInDb.SecurityStamp.Should().NotBe(postChangeSecurityStamp);

        _smsAlertService.LastRecipient.Should().Be(oldPhone);
        _smsAlertService.LastAlert!.Kind.Should().Be(SecurityAlertKind.PhoneRestored);

        recoverLogger.Entries.Should().NotBeEmpty();
        recoverLogger.AllText.Should().NotContain(oldPhone)
            .And.NotContain(newPhone)
            .And.NotContain(recoveryTokenRaw);
    }

    [Fact]
    public async Task RecoverPhone_After24Hours_ShouldRejectAsync()
    {
        using var dbContext = CreateDbContext();
        var recoverHandler = new RecoverPhoneHandler(
            dbContext, _tokenService, _smsAlertService, _clock, NullLogger<RecoverPhoneHandler>.Instance);

        var user = User.CreatePlayer(Guid.NewGuid(), "+201099998888", "Karim", _clock.UtcNow);
        dbContext.Users.Add(user);

        var recoveryTokenRaw = "my-secret-recovery-token-xyz";
        var recoveryTokenHash = _tokenService.HashRefreshToken(recoveryTokenRaw);

        var completedRequest = PhoneChangeRequest.Start(
            Guid.NewGuid(), user.Id, "+201011112222", "+201099998888", "old_hash", _clock.UtcNow.AddDays(-2), _clock.UtcNow.AddDays(-2));
        completedRequest.Complete(recoveryTokenHash, _clock.UtcNow.AddDays(-2)); // Completed 48 hours ago
        dbContext.PhoneChangeRequests.Add(completedRequest);

        await dbContext.SaveChangesAsync();

        var result = await recoverHandler.HandleAsync(new RecoverPhoneCommand(recoveryTokenRaw));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.RecoveryTokenExpired");
    }

    [Fact]
    public void PhoneChangeValidators_ShouldValidateCorrectly()
    {
        var startValidator = new StartPhoneChangeValidator();
        startValidator.Validate(new StartPhoneChangeCommand(Guid.NewGuid(), "01012345678", "token")).IsValid.Should().BeTrue();
        startValidator.Validate(new StartPhoneChangeCommand(Guid.Empty, "01012345678", "token")).IsValid.Should().BeFalse();
        startValidator.Validate(new StartPhoneChangeCommand(Guid.NewGuid(), "", "token")).IsValid.Should().BeFalse();
        startValidator.Validate(new StartPhoneChangeCommand(Guid.NewGuid(), "01012345678", "")).IsValid.Should().BeFalse();

        var verifyOldValidator = new VerifyOldPhoneValidator();
        verifyOldValidator.Validate(new VerifyOldPhoneCommand(Guid.NewGuid(), Guid.NewGuid(), "123456")).IsValid.Should().BeTrue();
        verifyOldValidator.Validate(new VerifyOldPhoneCommand(Guid.NewGuid(), Guid.NewGuid(), "12345")).IsValid.Should().BeFalse();

        var verifyNewValidator = new VerifyNewPhoneValidator();
        verifyNewValidator.Validate(new VerifyNewPhoneCommand(Guid.NewGuid(), Guid.NewGuid(), "123456")).IsValid.Should().BeTrue();
        verifyNewValidator.Validate(new VerifyNewPhoneCommand(Guid.NewGuid(), Guid.NewGuid(), "12345a")).IsValid.Should().BeFalse();

        var recoverValidator = new RecoverPhoneValidator();
        recoverValidator.Validate(new RecoverPhoneCommand("valid_token")).IsValid.Should().BeTrue();
        recoverValidator.Validate(new RecoverPhoneCommand("")).IsValid.Should().BeFalse();
    }

    private sealed class RecordingOtpSender : IOtpSender
    {
        public string LastRecipient { get; private set; } = string.Empty;
        public string LastCode { get; private set; } = string.Empty;
        public OtpPurpose? LastPurpose { get; private set; }

        public Task<bool> SendAsync(string phoneNumber, string code, OtpPurpose purpose, TimeSpan validity, CancellationToken cancellationToken = default)
        {
            LastRecipient = phoneNumber;
            LastCode = code;
            LastPurpose = purpose;
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingSmsAlertService : ISmsAlertService
    {
        public string LastRecipient { get; private set; } = string.Empty;
        public SecurityAlert? LastAlert { get; private set; }

        public Task SendSecurityAlertAsync(string phoneNumber, SecurityAlert alert, CancellationToken cancellationToken = default)
        {
            LastRecipient = phoneNumber;
            LastAlert = alert;
            return Task.CompletedTask;
        }
    }

    private sealed class MutableClock : IClock
    {
        private static readonly TimeZoneInfo _cairoZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        public MutableClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; private set; }
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairoZone);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairoZone;

        public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
    }
}
