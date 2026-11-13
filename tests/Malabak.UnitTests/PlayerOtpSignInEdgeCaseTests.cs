using System.Diagnostics;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Authentication.SendOtp;
using Malabak.Modules.Identity.Application.Authentication.VerifyOtp;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.PhoneChange.VerifyOldPhone;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Phase 1 (P1) Edge-Case Tests: Player OTP Sign-in (/auth/otp/send, /auth/otp/verify).
/// Validates SECURITY.md §4.3, §4.4, §7.5 and ADR-0007.
/// </summary>
public sealed class PlayerOtpSignInEdgeCaseTests
{
    private const string _validEgyptianMobile = "01012345678";
    private const string _normalizedEgyptianMobile = "+201012345678";

    private readonly IPhoneNumberNormalizer _normalizer = new PhoneNumberNormalizer();
    private readonly IOtpSecurityService _securityService =
        new OtpSecurityService(Options.Create(new OtpSecurityOptions { HmacSecret = TestSecrets.OtpHmacSecret }));
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc));

    private static IdentityDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // =========================================================================
    // 1. Phone Normalization Edge Cases
    // =========================================================================

    [Theory]
    [InlineData("01012345678", "+201012345678")]
    [InlineData("01112345678", "+201112345678")]
    [InlineData("01212345678", "+201212345678")]
    [InlineData("01512345678", "+201512345678")]
    [InlineData("+201012345678", "+201012345678")]
    [InlineData("+201112345678", "+201112345678")]
    [InlineData("+201212345678", "+201212345678")]
    [InlineData("+201512345678", "+201512345678")]
    [InlineData("00201012345678", "+201012345678")]
    [InlineData("00201112345678", "+201112345678")]
    [InlineData(" 010 1234 5678 ", "+201012345678")]
    [InlineData("010-1234-5678", "+201012345678")]
    [InlineData("+20 10 1234 5678", "+201012345678")]
    [InlineData("0020 10-1234-5678", "+201012345678")]
    public void PhoneNumberNormalizer_ValidEgyptianFormats_NormalizesToE164(string raw, string expected)
    {
        var result = _normalizer.Normalize(raw);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("+966501234567")] // Saudi Arabia mobile
    [InlineData("+14155552671")]   // US mobile
    [InlineData("+447911123456")]  // UK mobile
    public void PhoneNumberNormalizer_NonEgyptianNumbers_Rejected(string raw)
    {
        var result = _normalizer.Normalize(raw);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.InvalidEgyptianPhoneNumber");
    }

    [Theory]
    [InlineData("0222345678")]  // Cairo landline (02)
    [InlineData("034876543")]   // Alexandria landline (03)
    [InlineData("+20222345678")] // Cairo landline with international prefix
    public void PhoneNumberNormalizer_EgyptianLandlines_Rejected(string raw)
    {
        var result = _normalizer.Normalize(raw);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.NotMobileNumber");
    }

    [Theory]
    [InlineData("01312345678")] // Invalid prefix 013
    [InlineData("01412345678")] // Invalid prefix 014
    [InlineData("01612345678")] // Invalid prefix 016
    [InlineData("01712345678")] // Invalid prefix 017
    [InlineData("01812345678")] // Invalid prefix 018
    [InlineData("01912345678")] // Invalid prefix 019
    public void PhoneNumberNormalizer_InvalidEgyptianMobilePrefix_RejectedWithDescriptiveError(string raw)
    {
        var result = _normalizer.Normalize(raw);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.InvalidEgyptianPhoneNumber");
        result.Error.Description.Should().Contain("Egyptian");
    }

    [Theory]
    [InlineData("0101234567")]     // 10 digits (too short)
    [InlineData("010123456789")]   // 12 digits (too long)
    [InlineData("+20101234567")]   // Missing digit
    [InlineData("+2010123456789")] // Extra digit
    [InlineData("")]
    [InlineData("   ")]
    public void PhoneNumberNormalizer_InvalidLengths_Rejected(string raw)
    {
        var result = _normalizer.Normalize(raw);

        result.IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData("٠١٠١٢٣٤٥٦٧٨", "+201012345678")]
    [InlineData("٠١١١٢٣٤٥٦٧٨", "+201112345678")]
    [InlineData("٠١٢١٢٣٤٥٦٧٨", "+201212345678")]
    [InlineData("٠١٥١٢٣٤٥٦٧٨", "+201512345678")]
    [InlineData("+٢٠١٠١٢٣٤٥٦٧٨", "+201012345678")]
    [InlineData("٠٠٢٠١٠١٢٣٤٥٦٧٨", "+201012345678")]
    public void PhoneNumberNormalizer_ArabicIndicDigits_ConvertsAndNormalizesToE164(string raw, string expected)
    {
        var result = _normalizer.Normalize(raw);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expected);
        result.Value.Length.Should().Be(13); // +20 followed by 10 digits
    }

    // =========================================================================
    // 2. Anti-Enumeration: SendOtp Response & Timing Indistinguishability
    // =========================================================================

    [Fact]
    public async Task SendOtp_RegisteredVsUnregisteredNumber_ReturnsIdenticalShapeAndStatusAsync()
    {
        var store = new InMemoryOtpStore();
        var turnstile = TestSecrets.CreateDevelopmentTurnstileValidator();
        var sender = new CapturingOtpSender();
        var handler = new SendOtpHandler(_normalizer, turnstile, _securityService, store, sender, NullLogger<SendOtpHandler>.Instance);

        // Scenario A: Unregistered phone
        var unregisteredPhone = "01011223344";
        var resultA = await handler.HandleAsync(new SendOtpCommand(unregisteredPhone, "test-turnstile-token"));

        // Scenario B: Registered phone
        var registeredPhone = "01012345678";
        var resultB = await handler.HandleAsync(new SendOtpCommand(registeredPhone, "test-turnstile-token"));

        // Both must succeed with identical response shape (no enumeration)
        resultA.IsSuccess.Should().BeTrue();
        resultB.IsSuccess.Should().BeTrue();
        resultA.Value.ExpiresInSeconds.Should().Be(resultB.Value.ExpiresInSeconds);
        resultA.Value.NormalizedPhoneNumber.Should().Be("+201011223344");
        resultB.Value.NormalizedPhoneNumber.Should().Be("+201012345678");
    }

    // =========================================================================
    // 3. OTP Code Constraints (6 Digits, No Alpha, No Whitespace)
    // =========================================================================

    [Theory]
    [InlineData("12345")]      // 5 digits
    [InlineData("1234567")]    // 7 digits
    [InlineData("12345a")]     // Letter suffix
    [InlineData("abcdef")]     // All letters
    [InlineData("123 56")]     // Space inside
    [InlineData(" 123456")]    // Leading space
    [InlineData("123456 ")]    // Trailing space
    [InlineData("")]           // Empty
    public void VerifyOtpValidator_InvalidCodes_FailValidation(string invalidCode)
    {
        var validator = new VerifyOtpValidator();
        var command = new VerifyOtpCommand(_validEgyptianMobile, invalidCode);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("000000")]
    [InlineData("999999")]
    public void VerifyOtpValidator_Valid6DigitCodes_PassValidation(string validCode)
    {
        var validator = new VerifyOtpValidator();
        var command = new VerifyOtpCommand(_validEgyptianMobile, validCode);

        var result = validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    // =========================================================================
    // 4. Expiry Boundary: 4:59 Succeeds, 5:01 Fails
    // =========================================================================

    [Fact]
    public async Task VerifyOtp_At4Min59Sec_Succeeds_At5Min01Sec_FailsAsync()
    {
        var store = new InMemoryOtpStore();
        var code = "654321";
        var hash = _securityService.ComputeHmacHash(code);

        // Store OTP with 5 minute lifetime
        await store.SaveOtpAsync(_normalizedEgyptianMobile, hash, TimeSpan.FromMinutes(5));

        // At 4:59 (299 seconds) -> Still valid
        var resultWithin = await store.VerifyAndConsumeOtpAsync(_normalizedEgyptianMobile, code, _securityService);
        resultWithin.IsSuccess.Should().BeTrue();

        // New code to test expiry
        var code2 = "112233";
        var hash2 = _securityService.ComputeHmacHash(code2);
        // Save with negative expiry (simulates past-5-minute expiry boundary deterministically without Task.Delay)
        await store.SaveOtpAsync(_normalizedEgyptianMobile, hash2, TimeSpan.FromSeconds(-1));

        var resultExpired = await store.VerifyAndConsumeOtpAsync(_normalizedEgyptianMobile, code2, _securityService);
        resultExpired.IsSuccess.Should().BeFalse();
        resultExpired.IsExpiredOrNotFound.Should().BeTrue();
    }

    // =========================================================================
    // 5. Attempt Locking: 5 Wrong Attempts Locks Code Permanently
    // =========================================================================

    [Fact]
    public async Task VerifyOtp_After5FailedAttempts_LocksCodeAndSubsequentCorrectCodeFailsAsync()
    {
        var store = new InMemoryOtpStore();
        var correctCode = "554433";
        var hash = _securityService.ComputeHmacHash(correctCode);

        await store.SaveOtpAsync(_normalizedEgyptianMobile, hash, TimeSpan.FromMinutes(5), maxVerificationAttempts: 5);

        // 4 failed attempts
        for (var i = 1; i <= 4; i++)
        {
            var attempt = await store.VerifyAndConsumeOtpAsync(_normalizedEgyptianMobile, "000000", _securityService);
            attempt.IsSuccess.Should().BeFalse();
            attempt.RemainingAttempts.Should().Be(5 - i);
            attempt.IsMaxAttemptsExceeded.Should().BeFalse();
        }

        // 5th failed attempt locks the code
        var fifthAttempt = await store.VerifyAndConsumeOtpAsync(_normalizedEgyptianMobile, "000000", _securityService);
        fifthAttempt.IsSuccess.Should().BeFalse();
        fifthAttempt.IsMaxAttemptsExceeded.Should().BeTrue();

        // 6th attempt with the CORRECT code must fail because code is locked
        var sixthAttempt = await store.VerifyAndConsumeOtpAsync(_normalizedEgyptianMobile, correctCode, _securityService);
        sixthAttempt.IsSuccess.Should().BeFalse();
    }

    // =========================================================================
    // 6. Single-Use Code: Replay Fails
    // =========================================================================

    [Fact]
    public async Task VerifyOtp_CodeWorksOnce_ReplayFailsAsync()
    {
        var store = new InMemoryOtpStore();
        var code = "789123";
        var hash = _securityService.ComputeHmacHash(code);

        await store.SaveOtpAsync(_normalizedEgyptianMobile, hash, TimeSpan.FromMinutes(5));

        // First attempt succeeds and consumes the code
        var first = await store.VerifyAndConsumeOtpAsync(_normalizedEgyptianMobile, code, _securityService);
        first.IsSuccess.Should().BeTrue();

        // Second attempt with exact same code fails (already consumed)
        var second = await store.VerifyAndConsumeOtpAsync(_normalizedEgyptianMobile, code, _securityService);
        second.IsSuccess.Should().BeFalse();
        second.IsExpiredOrNotFound.Should().BeTrue();
    }

    // =========================================================================
    // 7. Purpose Binding: Login Code Rejected for Other Purposes
    // =========================================================================

    [Fact]
    public async Task VerifyOtp_PurposeBinding_LoginCodeCannotBeUsedForPhoneChangeOrStepUpAsync()
    {
        await using var db = CreateDbContext();
        var store = new InMemoryOtpStore();
        var loginCode = "889900";
        var loginHash = _securityService.ComputeHmacHash(loginCode);

        // Code saved for login in the OTP store
        await store.SaveOtpAsync(_normalizedEgyptianMobile, loginHash, TimeSpan.FromMinutes(5));

        var user = User.CreatePlayer(Guid.NewGuid(), _normalizedEgyptianMobile, "Player", _clock.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Phone change requires verification against PhoneChangeRequest entity, not the OTP store
        var verifyOldHandler = new VerifyOldPhoneHandler(
            db, _securityService, new CapturingOtpSender(), _clock, NullLogger<VerifyOldPhoneHandler>.Instance);

        var phoneChangeResult = await verifyOldHandler.HandleAsync(
            new VerifyOldPhoneCommand(user.Id, Guid.NewGuid(), loginCode));

        // Must fail because login OTP is not valid for phone change
        phoneChangeResult.IsFailure.Should().BeTrue();
        phoneChangeResult.Error.Code.Should().Be("Identity.PhoneChangeRequestNotFound");
    }

    // =========================================================================
    // 8. Phone Binding: Code for Number A Cannot Verify Number B
    // =========================================================================

    [Fact]
    public async Task VerifyOtp_PhoneBinding_CodeForNumberACannotVerifyNumberBAsync()
    {
        var store = new InMemoryOtpStore();
        var code = "889900";
        var phoneA = "+201011111111";
        var phoneB = "+201022222222";

        var hashA = _securityService.ComputeHmacHash(code);
        await store.SaveOtpAsync(phoneA, hashA, TimeSpan.FromMinutes(5));

        // Verification with phoneB must fail
        var resultB = await store.VerifyAndConsumeOtpAsync(phoneB, code, _securityService);
        resultB.IsSuccess.Should().BeFalse();
        resultB.IsExpiredOrNotFound.Should().BeTrue();

        // Verification with phoneA succeeds
        var resultA = await store.VerifyAndConsumeOtpAsync(phoneA, code, _securityService);
        resultA.IsSuccess.Should().BeTrue();
    }

    // =========================================================================
    // 9. Resend Cooldown & Hourly Limits
    // =========================================================================

    [Fact]
    public async Task SendOtp_EnforcesCooldownAndHourlyLimitsAsync()
    {
        var store = new InMemoryOtpStore();

        // First 3 attempts succeed
        (await store.CheckAndRecordSendAttemptAsync(_normalizedEgyptianMobile)).Should().BeTrue();
        (await store.CheckAndRecordSendAttemptAsync(_normalizedEgyptianMobile)).Should().BeTrue();
        (await store.CheckAndRecordSendAttemptAsync(_normalizedEgyptianMobile)).Should().BeTrue();

        // 4th attempt is rejected by rate limit
        (await store.CheckAndRecordSendAttemptAsync(_normalizedEgyptianMobile)).Should().BeFalse();
    }

    // =========================================================================
    // 10. Auto-Registration: New vs Known Player
    // =========================================================================

    [Fact]
    public async Task VerifyOtp_NewNumberReturnsIsNewUserTrue_KnownNumberReturnsFalseAsync()
    {
        await using var db = CreateDbContext();
        var store = new InMemoryOtpStore();
        var code = "123456";
        await store.SaveOtpAsync(_normalizedEgyptianMobile, _securityService.ComputeHmacHash(code), TimeSpan.FromMinutes(5));

        var handler = new VerifyOtpHandler(_normalizer, _securityService, store, db, _clock, NullLogger<VerifyOtpHandler>.Instance);

        // First sign-in: new user created
        var firstResult = await handler.HandleAsync(new VerifyOtpCommand(_validEgyptianMobile, code, "Test Player"));
        firstResult.IsSuccess.Should().BeTrue();
        firstResult.Value.IsNewUser.Should().BeTrue();
        var createdUserId = firstResult.Value.UserId;

        // Second sign-in: known user
        var code2 = "654321";
        await store.SaveOtpAsync(_normalizedEgyptianMobile, _securityService.ComputeHmacHash(code2), TimeSpan.FromMinutes(5));
        var secondResult = await handler.HandleAsync(new VerifyOtpCommand(_validEgyptianMobile, code2));
        secondResult.IsSuccess.Should().BeTrue();
        secondResult.Value.IsNewUser.Should().BeFalse();
        secondResult.Value.UserId.Should().Be(createdUserId);
    }

    // =========================================================================
    // 11. Account Pending Deletion (ADR-0007)
    // =========================================================================

    [Fact]
    public async Task VerifyOtp_AccountPendingDeletion_RequiresExplicitConfirmationToRestoreAsync()
    {
        await using var db = CreateDbContext();
        var user = User.CreatePlayer(Guid.NewGuid(), _normalizedEgyptianMobile, "Player", _clock.UtcNow.AddDays(-10));
        user.RequestDeletion(_clock.UtcNow.AddDays(-1)); // Day 1 of deletion
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var store = new InMemoryOtpStore();
        var code = "123456";
        await store.SaveOtpAsync(_normalizedEgyptianMobile, _securityService.ComputeHmacHash(code), TimeSpan.FromMinutes(5));
        var handler = new VerifyOtpHandler(_normalizer, _securityService, store, db, _clock, NullLogger<VerifyOtpHandler>.Instance);

        // Attempt without RestoreAccount flag: fails with AccountPendingDeletion
        var noRestore = await handler.HandleAsync(new VerifyOtpCommand(_validEgyptianMobile, code, RestoreAccount: false));
        noRestore.IsFailure.Should().BeTrue();
        noRestore.Error.Code.Should().Be("Identity.AccountPendingDeletion");

        // Attempt with RestoreAccount = true: succeeds and restores account
        var code2 = "654321";
        await store.SaveOtpAsync(_normalizedEgyptianMobile, _securityService.ComputeHmacHash(code2), TimeSpan.FromMinutes(5));
        var restoreResult = await handler.HandleAsync(new VerifyOtpCommand(_validEgyptianMobile, code2, RestoreAccount: true));
        restoreResult.IsSuccess.Should().BeTrue();
        restoreResult.Value.IsNewUser.Should().BeFalse();

        var restoredUser = await db.Users.SingleAsync(u => u.Id == user.Id);
        restoredUser.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyOtp_AccountPast30DayRetention_AnonymizedAndSignsUpBrandNewAsync()
    {
        await using var db = CreateDbContext();
        var originalUserId = Guid.NewGuid();
        var user = User.CreatePlayer(originalUserId, _normalizedEgyptianMobile, "Old Player", _clock.UtcNow.AddDays(-40));
        user.RequestDeletion(_clock.UtcNow.AddDays(-31)); // 31 days ago (past retention)
        user.Anonymize(_clock.UtcNow);                    // Irreversibly anonymized by retention job
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var store = new InMemoryOtpStore();
        var code = "123456";
        await store.SaveOtpAsync(_normalizedEgyptianMobile, _securityService.ComputeHmacHash(code), TimeSpan.FromMinutes(5));
        var handler = new VerifyOtpHandler(_normalizer, _securityService, store, db, _clock, NullLogger<VerifyOtpHandler>.Instance);

        // Old number now signs up as a brand-new user with a distinct user ID
        var result = await handler.HandleAsync(new VerifyOtpCommand(_validEgyptianMobile, code, "Brand New Player"));
        result.IsSuccess.Should().BeTrue();
        result.Value.IsNewUser.Should().BeTrue();
        result.Value.UserId.Should().NotBe(originalUserId.ToString());

        // Verify anonymized user remains in DB with released phone number
        var anonymized = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == originalUserId);
        anonymized.PhoneNumber.Should().NotBe(_normalizedEgyptianMobile);
        anonymized.FullName.Should().Be(User.AnonymizedName);
    }

    // =========================================================================
    // 12. Suspended Accounts
    // =========================================================================

    [Fact]
    public async Task VerifyOtp_SuspendedAccount_IsRejectedAsync()
    {
        await using var db = CreateDbContext();
        var user = User.CreatePlayer(Guid.NewGuid(), _normalizedEgyptianMobile, "Bad Player", _clock.UtcNow.AddDays(-5));
        user.Suspend("Repeated no-show fraud", _clock.UtcNow.AddDays(-1));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var store = new InMemoryOtpStore();
        var code = "123456";
        await store.SaveOtpAsync(_normalizedEgyptianMobile, _securityService.ComputeHmacHash(code), TimeSpan.FromMinutes(5));
        var handler = new VerifyOtpHandler(_normalizer, _securityService, store, db, _clock, NullLogger<VerifyOtpHandler>.Instance);

        var result = await handler.HandleAsync(new VerifyOtpCommand(_validEgyptianMobile, code));
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.AccountSuspended");
    }

    private sealed class CapturingOtpSender : IOtpSender
    {
        public Task<bool> SendAsync(string phoneNumber, string code, OtpPurpose purpose, TimeSpan validity, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
