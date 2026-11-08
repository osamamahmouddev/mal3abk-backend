using FluentAssertions;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Domain.Events;
using Malabak.Modules.Identity.Application.Authentication.SendOtp;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// TASK-802: Abuse and rate-limit simulation tests.
/// Simulates OTP pumping, brute force, slot hoarding, scraping, and security alert triggers.
/// </summary>
public sealed class AbuseAndRateLimitSimulationTests
{
    private readonly PhoneNumberNormalizer _phoneNormalizer = new();

    [Fact]
    public async Task OtpPumpingSimulation_RapidRequestsForSamePhone_ThrottledWithRetryAfterAsync()
    {
        // Arrange: simulate SMS pumping attack against single destination
        var securityService = new OtpSecurityService(Options.Create(new OtpSecurityOptions { HmacSecret = TestSecrets.OtpHmacSecret }));
        var store = new InMemoryOtpStore();
        var handler = new SendOtpHandler(
            _phoneNormalizer,
            TestSecrets.CreateDevelopmentTurnstileValidator(),
            securityService,
            store,
            new MockOtpSender(),
            NullLogger<SendOtpHandler>.Instance);

        var phone = "01099887766";
        var command = new SendOtpCommand(phone, "test-turnstile-token");

        // Act: 3 allowed requests within window
        for (var i = 0; i < 3; i++)
        {
            var allowedResult = await handler.HandleAsync(command);
            allowedResult.IsSuccess.Should().BeTrue();
        }

        // 4th request must be rejected to prevent SMS toll fraud / budget exhaustion
        var throttledResult = await handler.HandleAsync(command);

        // Assert
        throttledResult.IsFailure.Should().BeTrue();
        throttledResult.Error.Code.Should().Be("Identity.RateLimitExceeded");
        throttledResult.Error.RetryAfter.Should().NotBeNull();
        throttledResult.Error.RetryAfter!.Value.TotalSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task OtpPumpingSimulation_WithoutValidTurnstile_RejectsImmediatelyAsync()
    {
        // Arrange: simulating automated bot without Turnstile token
        var securityService = new OtpSecurityService(Options.Create(new OtpSecurityOptions { HmacSecret = TestSecrets.OtpHmacSecret }));
        var handler = new SendOtpHandler(
            _phoneNormalizer,
            new RejectingTurnstileValidator(),
            securityService,
            new InMemoryOtpStore(),
            new MockOtpSender(),
            NullLogger<SendOtpHandler>.Instance);

        var command = new SendOtpCommand("01011223344", "invalid-bot-token");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.InvalidTurnstileToken");
    }

    [Fact]
    public async Task BruteForceSimulation_MultipleFailedAttempts_ExceedsMaxAttemptsAsync()
    {
        // Arrange: simulate an attacker attempting to brute-force a 6-digit OTP code
        var securityService = new OtpSecurityService(Options.Create(new OtpSecurityOptions { HmacSecret = TestSecrets.OtpHmacSecret }));
        var store = new InMemoryOtpStore();
        var phone = "+201099887766";
        var correctCode = "123456";
        var hash = securityService.ComputeHmacHash(correctCode);

        await store.SaveOtpAsync(phone, hash, TimeSpan.FromMinutes(5), maxVerificationAttempts: 5);

        // Act: simulate 5 consecutive wrong guesses
        OtpVerificationResult? lastResult = null;
        for (var i = 0; i < 5; i++)
        {
            var wrongCode = $"99999{i}";
            lastResult = await store.VerifyAndConsumeOtpAsync(phone, wrongCode, securityService);
            lastResult.IsSuccess.Should().BeFalse();
        }

        // Assert: 5th failed attempt triggers IsMaxAttemptsExceeded lockout
        lastResult.Should().NotBeNull();
        lastResult!.IsMaxAttemptsExceeded.Should().BeTrue();
        lastResult.RemainingAttempts.Should().Be(0);

        // Subsequent verification attempt is blocked because OTP was evicted upon reaching max attempts
        var lockedResult = await store.VerifyAndConsumeOtpAsync(phone, correctCode, securityService);
        lockedResult.IsSuccess.Should().BeFalse();
        lockedResult.IsExpiredOrNotFound.Should().BeTrue();
    }

    [Fact]
    public void SlotHoardingSimulation_ExpiredPendingHold_TransitionsToExpired()
    {
        // Arrange: simulate an attacker hoarding prime slots without completing payment
        var now = new DateTime(2026, 11, 8, 20, 0, 0, DateTimeKind.Utc);
        var startUtc = now.AddHours(2);
        var endUtc = now.AddHours(3);
        var holdExpiresAtUtc = now.AddMinutes(15);

        var priceSnapshot = new PriceQuoteSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            startUtc,
            endUtc,
            60,
            "EGP",
            500m,
            0m,
            null,
            500m,
            100m,
            "Fixed",
            [new PriceLineSnapshot(startUtc, endUtc, 500m, 500m, [])],
            now);

        var policySnapshot = new BookingPolicySnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "Fixed",
            100m,
            15,
            120,
            24,
            "FullRefund",
            100m,
            "ForfeitDeposit",
            15,
            true,
            24,
            true,
            "Standard policy",
            now);

        var booking = Booking.Create(
            Guid.NewGuid(),
            "MLB-HOARD1",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            startUtc,
            endUtc,
            new Money(500m),
            new Money(100m),
            holdExpiresAtUtc,
            priceSnapshot,
            policySnapshot,
            now,
            "bot-attacker").Value;

        // Act: simulate hold timeout passing
        var expireResult = booking.Expire(now.AddMinutes(16));

        // Assert: slot is released from held status so legitimate players can book it
        expireResult.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Expired);
        booking.DomainEvents.Should().ContainSingle(e => e is BookingExpiredDomainEvent);
    }

    [Fact]
    public void SecurityAlert_OnSensitiveAction_DispatchesWithoutPiiLeak()
    {
        // Arrange: security alerts must mask phone numbers and contain no sensitive keys
        var oldPhone = "+201012345678";
        var newPhone = "+201087654321";
        var recoveryUrl = "https://malabak.app/recover?token=sec-tok-99";
        var alert = SecurityAlert.PhoneChanged(newPhone, recoveryUrl);

        var message = SlackSmsAlertService.BuildMessage(oldPhone, alert, "Production");

        // Assert: message masks both phones and preserves token url
        message.Should().Contain("+2010•••••678");
        message.Should().Contain("+2010•••••321");
        message.Should().NotContain(oldPhone);
        message.Should().NotContain(newPhone);
        message.Should().Contain(recoveryUrl);
        message.Should().Contain("Production");
    }

    private sealed class MockOtpSender : IOtpSender
    {
        public Task<bool> SendAsync(string phoneNumber, string code, OtpPurpose purpose, TimeSpan validity, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class RejectingTurnstileValidator : ITurnstileValidator
    {
        public Task<bool> ValidateTokenAsync(string? turnstileToken, string? remoteIp = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
