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
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Phase 1 (P1) Edge-Case Tests: Phone Change State Machine (SECURITY.md §5).
/// Validates step-up binding, strict multi-step order, cooldown, session revocation, anti-enumeration, and recovery.
/// </summary>
public sealed class PhoneChangeEdgeCaseTests
{
    private const string _currentPhone = "+201011112222";
    private const string _targetNewPhoneRaw = "01099998888";
    private const string _targetNewPhoneNormalized = "+201099998888";

    private readonly FakeClock _clock = new(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc));
    private readonly IPhoneNumberNormalizer _normalizer = new PhoneNumberNormalizer();
    private readonly IOtpSecurityService _otpSecurityService;
    private readonly IStepUpService _stepUpService;
    private readonly ITokenService _tokenService;
    private readonly CapturingSmsAlertService _smsAlertService = new();
    private readonly CapturingOtpSender _otpSender = new();

    public PhoneChangeEdgeCaseTests()
    {
        _otpSecurityService = new OtpSecurityService(Options.Create(new OtpSecurityOptions { HmacSecret = TestSecrets.OtpHmacSecret }));
        _stepUpService = new StepUpService(Options.Create(new StepUpOptions { SecretKey = TestSecrets.StepUpSecretKey }), _clock);

        var jwtOptions = Options.Create(new JwtOptions { CurrentKeyId = "test-key-v1" });
        _tokenService = new TokenService(new JwtKeyProvider(jwtOptions), _clock, jwtOptions);
    }

    private static IdentityDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private (StartPhoneChangeHandler Start, VerifyOldPhoneHandler VerifyOld, VerifyNewPhoneHandler VerifyNew, RecoverPhoneHandler Recover)
        CreateHandlers(IdentityDbContext db)
    {
        var start = new StartPhoneChangeHandler(db, _stepUpService, _normalizer, _otpSecurityService, _otpSender, _clock, NullLogger<StartPhoneChangeHandler>.Instance);
        var verifyOld = new VerifyOldPhoneHandler(db, _otpSecurityService, _otpSender, _clock, NullLogger<VerifyOldPhoneHandler>.Instance);
        var verifyNew = new VerifyNewPhoneHandler(db, _otpSecurityService, _tokenService, _smsAlertService, _clock, NullLogger<VerifyNewPhoneHandler>.Instance);
        var recover = new RecoverPhoneHandler(db, _tokenService, _smsAlertService, _clock, NullLogger<RecoverPhoneHandler>.Instance);
        return (start, verifyOld, verifyNew, recover);
    }

    // =========================================================================
    // 1. Step-Up Requirement & Current Phone Validation (SECURITY.md §5.2)
    // =========================================================================

    [Fact]
    public async Task StartPhoneChange_WithoutValidStepUpToken_RejectedAsync()
    {
        await using var db = CreateDbContext();
        var user = User.CreatePlayer(Guid.NewGuid(), _currentPhone, "Player", _clock.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var (start, _, _, _) = CreateHandlers(db);

        // Missing / invalid step-up token
        var result = await start.HandleAsync(new StartPhoneChangeCommand(user.Id, _targetNewPhoneRaw, "invalid-step-up-token"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.InvalidStepUpToken");
    }

    [Fact]
    public async Task StartPhoneChange_With_currentPhoneNumber_RejectedAsync()
    {
        await using var db = CreateDbContext();
        var user = User.CreatePlayer(Guid.NewGuid(), _currentPhone, "Player", _clock.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var (start, _, _, _) = CreateHandlers(db);
        var stepUp = _stepUpService.IssueStepUpToken(user.Id, "change_phone");

        // Attempting to change to current phone number
        var result = await start.HandleAsync(new StartPhoneChangeCommand(user.Id, _currentPhone, stepUp));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.SamePhoneNumber");
    }

    [Fact(Skip = "BUG-004: StartPhoneChange returns PhoneNumberAlreadyInUse revealing account existence instead of generic error")]
    public async Task StartPhoneChange_WithNumberUsedByAnotherAccount_MustReturnGenericErrorAsync()
    {
        await using var db = CreateDbContext();
        var userA = User.CreatePlayer(Guid.NewGuid(), _currentPhone, "Player A", _clock.UtcNow);
        var userB = User.CreatePlayer(Guid.NewGuid(), _targetNewPhoneNormalized, "Player B", _clock.UtcNow);
        db.Users.AddRange(userA, userB);
        await db.SaveChangesAsync();

        var (start, _, _, _) = CreateHandlers(db);
        var stepUp = _stepUpService.IssueStepUpToken(userA.Id, "change_phone");

        // SECURITY.md §5.2 rule 6: Must return generic error without revealing whether number is registered
        var result = await start.HandleAsync(new StartPhoneChangeCommand(userA.Id, _targetNewPhoneRaw, stepUp));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.UnableToUsePhoneNumber");
    }

    [Fact(Skip = "BUG-005: Concurrent phone changes to the same number trigger unhandled DbUpdateException (500)")]
    public async Task CompletePhoneChange_ConcurrentRequestsForSameNumber_MustNotThrow500Async()
    {
        await using var db = CreateDbContext();
        var (_, _, verifyNew, _) = CreateHandlers(db);

        var userA = User.CreatePlayer(Guid.NewGuid(), "+201011112222", "User A", _clock.UtcNow);
        var userB = User.CreatePlayer(Guid.NewGuid(), "+201033334444", "User B", _clock.UtcNow);
        db.Users.AddRange(userA, userB);
        await db.SaveChangesAsync();

        var code = "123456";
        var hash = _otpSecurityService.ComputeHmacHash(code);

        var reqA = PhoneChangeRequest.Start(
            Guid.NewGuid(), userA.Id, "+201011112222", "+201099998888", "old-hash", _clock.UtcNow.AddMinutes(15), _clock.UtcNow);
        reqA.MarkOldPhoneVerified(hash, _clock.UtcNow);

        var reqB = PhoneChangeRequest.Start(
            Guid.NewGuid(), userB.Id, "+201033334444", "+201099998888", "old-hash", _clock.UtcNow.AddMinutes(15), _clock.UtcNow);
        reqB.MarkOldPhoneVerified(hash, _clock.UtcNow);

        db.PhoneChangeRequests.AddRange(reqA, reqB);
        await db.SaveChangesAsync();

        var resultA = await verifyNew.HandleAsync(new VerifyNewPhoneCommand(userA.Id, reqA.Id, code));
        var resultB = await verifyNew.HandleAsync(new VerifyNewPhoneCommand(userB.Id, reqB.Id, code));

        resultA.IsSuccess.Should().BeTrue();
        resultB.IsFailure.Should().BeTrue();
    }

    // =========================================================================
    // 2. Strict State Transition Order: verify-new Before verify-old Rejected
    // =========================================================================

    [Fact]
    public async Task VerifyNewPhone_BeforeVerifyOldPhone_RejectedAsync()
    {
        await using var db = CreateDbContext();
        var user = User.CreatePlayer(Guid.NewGuid(), _currentPhone, "Player", _clock.UtcNow);
        db.Users.Add(user);

        var request = PhoneChangeRequest.Start(
            Guid.NewGuid(), user.Id, _currentPhone, _targetNewPhoneNormalized, "old-hash", _clock.UtcNow.AddMinutes(15), _clock.UtcNow);
        db.PhoneChangeRequests.Add(request);
        await db.SaveChangesAsync();

        var (_, _, verifyNew, _) = CreateHandlers(db);

        // Attempting verify-new while state is still Started (old phone not yet verified)
        var result = await verifyNew.HandleAsync(new VerifyNewPhoneCommand(user.Id, request.Id, "123456"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.InvalidPhoneChangeState");
        result.Error.Description.Should().Contain("Old phone number must be verified before");
    }

    // =========================================================================
    // 3. Expiration: After 15 Minutes Request Expires
    // =========================================================================

    [Fact]
    public async Task VerifyOldPhone_After15Minutes_ExpiresAsync()
    {
        await using var db = CreateDbContext();
        var user = User.CreatePlayer(Guid.NewGuid(), _currentPhone, "Player", _clock.UtcNow);
        db.Users.Add(user);

        var request = PhoneChangeRequest.Start(
            Guid.NewGuid(), user.Id, _currentPhone, _targetNewPhoneNormalized, "old-hash", _clock.UtcNow.AddMinutes(15), _clock.UtcNow);
        db.PhoneChangeRequests.Add(request);
        await db.SaveChangesAsync();

        var (_, verifyOld, _, _) = CreateHandlers(db);

        // Advance past 15 minute lifetime
        _clock.Advance(TimeSpan.FromMinutes(16));

        var result = await verifyOld.HandleAsync(new VerifyOldPhoneCommand(user.Id, request.Id, "123456"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.PhoneChangeRequestExpired");

        var inDb = await db.PhoneChangeRequests.SingleAsync(r => r.Id == request.Id);
        inDb.Status.Should().Be(PhoneChangeStatus.Expired);
    }

    [Fact(Skip = "BUG-006: PhoneChangeRequest does not track or limit failed attempts, permitting brute-force within 15 minutes")]
    public async Task VerifyOldPhone_After5FailedAttempts_LocksRequestAsFailedAsync()
    {
        await using var db = CreateDbContext();
        var user = User.CreatePlayer(Guid.NewGuid(), _currentPhone, "Player", _clock.UtcNow);
        db.Users.Add(user);

        var correctCode = "123456";
        var oldHash = _otpSecurityService.ComputeHmacHash(correctCode);
        var request = PhoneChangeRequest.Start(
            Guid.NewGuid(), user.Id, _currentPhone, _targetNewPhoneNormalized, oldHash, _clock.UtcNow.AddMinutes(15), _clock.UtcNow);
        db.PhoneChangeRequests.Add(request);
        await db.SaveChangesAsync();

        var (_, verifyOld, _, _) = CreateHandlers(db);

        // 5 consecutive wrong attempts
        for (var i = 0; i < 5; i++)
        {
            await verifyOld.HandleAsync(new VerifyOldPhoneCommand(user.Id, request.Id, "000000"));
        }

        // 6th attempt with CORRECT code must fail because request is locked/failed
        var lockedResult = await verifyOld.HandleAsync(new VerifyOldPhoneCommand(user.Id, request.Id, correctCode));
        lockedResult.IsFailure.Should().BeTrue();
        lockedResult.Error.Code.Should().Be("Identity.PhoneChangeMaxAttemptsExceeded");
    }

    // =========================================================================
    // 4. 7-Day Cooldown (SECURITY.md §5.2 rule 6)
    // =========================================================================

    [Fact]
    public async Task StartPhoneChange_Enforces7DayCooldownAfterSuccessfulChangeAsync()
    {
        await using var db = CreateDbContext();
        var user = User.CreatePlayer(Guid.NewGuid(), _currentPhone, "Player", _clock.UtcNow);
        // Changed phone 3 days ago
        user.ChangePhoneNumber(_currentPhone, _clock.UtcNow.AddDays(-3));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var (start, _, _, _) = CreateHandlers(db);
        var stepUp = _stepUpService.IssueStepUpToken(user.Id, "change_phone");

        // Attempting to start another change before 7 days elapse
        var result = await start.HandleAsync(new StartPhoneChangeCommand(user.Id, _targetNewPhoneRaw, stepUp));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.PhoneChangeCooldownActive");

        // After 8 days (past cooldown)
        _clock.Advance(TimeSpan.FromDays(5));
        var freshStepUp = _stepUpService.IssueStepUpToken(user.Id, "change_phone");
        var allowedResult = await start.HandleAsync(new StartPhoneChangeCommand(user.Id, _targetNewPhoneRaw, freshStepUp));

        allowedResult.IsSuccess.Should().BeTrue();
    }

    // =========================================================================
    // 5. Completion: Revokes Other Sessions & Delivers Recovery Token to Old Phone
    // =========================================================================

    [Fact]
    public async Task CompletePhoneChange_RevokesExistingSessionsAndRotatesSecurityStampAsync()
    {
        await using var db = CreateDbContext();
        var user = User.CreatePlayer(Guid.NewGuid(), _currentPhone, "Player", _clock.UtcNow);
        var originalStamp = user.SecurityStamp;
        db.Users.Add(user);

        var activeToken = RefreshToken.Create(Guid.NewGuid(), user.Id, "hash1", Guid.NewGuid(), _clock.UtcNow.AddDays(7), _clock.UtcNow);
        db.RefreshTokens.Add(activeToken);

        var newCode = "654321";
        var newHash = _otpSecurityService.ComputeHmacHash(newCode);

        var request = PhoneChangeRequest.Start(
            Guid.NewGuid(), user.Id, _currentPhone, _targetNewPhoneNormalized, "old-hash", _clock.UtcNow.AddMinutes(15), _clock.UtcNow);
        request.MarkOldPhoneVerified(newHash, _clock.UtcNow);
        db.PhoneChangeRequests.Add(request);
        await db.SaveChangesAsync();

        var (_, _, verifyNew, _) = CreateHandlers(db);

        var result = await verifyNew.HandleAsync(new VerifyNewPhoneCommand(user.Id, request.Id, newCode));

        result.IsSuccess.Should().BeTrue();

        // Security stamp must rotate
        var updatedUser = await db.Users.SingleAsync(u => u.Id == user.Id);
        updatedUser.PhoneNumber.Should().Be(_targetNewPhoneNormalized);
        updatedUser.SecurityStamp.Should().NotBe(originalStamp);

        // Active refresh tokens must be revoked
        var tokenInDb = await db.RefreshTokens.SingleAsync(t => t.Id == activeToken.Id);
        tokenInDb.IsRevoked.Should().BeTrue();

        // Alert sent to OLD phone with recovery link
        _smsAlertService.LastRecipient.Should().Be(_currentPhone);
        _smsAlertService.LastAlert!.Kind.Should().Be(SecurityAlertKind.PhoneChanged);
        _smsAlertService.LastAlert.RecoveryLink.Should().Contain("recover-phone?token=");
    }

    // =========================================================================
    // 6. 24-Hour Recovery Window (SECURITY.md §5.5)
    // =========================================================================

    [Fact]
    public async Task RecoverPhone_Within24Hours_RestoresOldPhoneAndRotatesStampAsync()
    {
        await using var db = CreateDbContext();
        var user = User.CreatePlayer(Guid.NewGuid(), _targetNewPhoneNormalized, "Player", _clock.UtcNow);
        user.ChangePhoneNumber(_targetNewPhoneNormalized, _clock.UtcNow);
        var postChangeStamp = user.SecurityStamp;
        db.Users.Add(user);

        const string recoveryToken = "test-recovery-token-xyz-123";
        var tokenHash = _tokenService.HashRefreshToken(recoveryToken);

        var completedRequest = PhoneChangeRequest.Start(
            Guid.NewGuid(), user.Id, _currentPhone, _targetNewPhoneNormalized, "old-hash", _clock.UtcNow.AddMinutes(15), _clock.UtcNow.AddHours(-1));
        completedRequest.MarkOldPhoneVerified("new-hash", _clock.UtcNow.AddHours(-1));
        completedRequest.Complete(tokenHash, _clock.UtcNow.AddHours(-1));
        db.PhoneChangeRequests.Add(completedRequest);
        await db.SaveChangesAsync();

        var (_, _, _, recover) = CreateHandlers(db);

        // Recover within 24 hours
        var result = await recover.HandleAsync(new RecoverPhoneCommand(recoveryToken));

        result.IsSuccess.Should().BeTrue();
        var restoredUser = await db.Users.SingleAsync(u => u.Id == user.Id);
        restoredUser.PhoneNumber.Should().Be(_currentPhone);
        restoredUser.SecurityStamp.Should().NotBe(postChangeStamp);
    }

    [Fact]
    public async Task RecoverPhone_After24Hours_RejectedAsync()
    {
        await using var db = CreateDbContext();
        var user = User.CreatePlayer(Guid.NewGuid(), _targetNewPhoneNormalized, "Player", _clock.UtcNow);
        db.Users.Add(user);

        const string recoveryToken = "expired-recovery-token-456";
        var tokenHash = _tokenService.HashRefreshToken(recoveryToken);

        // Completed 25 hours ago (past 24-hour recovery window)
        var completedRequest = PhoneChangeRequest.Start(
            Guid.NewGuid(), user.Id, _currentPhone, _targetNewPhoneNormalized, "old-hash", _clock.UtcNow.AddMinutes(15), _clock.UtcNow.AddHours(-25));
        completedRequest.MarkOldPhoneVerified("new-hash", _clock.UtcNow.AddHours(-25));
        completedRequest.Complete(tokenHash, _clock.UtcNow.AddHours(-25));
        db.PhoneChangeRequests.Add(completedRequest);
        await db.SaveChangesAsync();

        var (_, _, _, recover) = CreateHandlers(db);

        var result = await recover.HandleAsync(new RecoverPhoneCommand(recoveryToken));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.RecoveryTokenExpired");
    }

    // =========================================================================
    // Test Doubles
    // =========================================================================

    private sealed class CapturingSmsAlertService : ISmsAlertService
    {
        public string? LastRecipient { get; private set; }
        public SecurityAlert? LastAlert { get; private set; }

        public Task SendSecurityAlertAsync(string phoneNumber, SecurityAlert alert, CancellationToken cancellationToken = default)
        {
            LastRecipient = phoneNumber;
            LastAlert = alert;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingOtpSender : IOtpSender
    {
        public Task<bool> SendAsync(string phoneNumber, string code, OtpPurpose purpose, TimeSpan validity, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
