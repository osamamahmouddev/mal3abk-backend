using System.Security.Claims;
using FluentAssertions;
using Malabak.Api.Infrastructure.Logging;
using Malabak.Modules.Audit.Application;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Application.Bookings.CancelBooking;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.Modules.Identity.Application.Authentication.SendOtp;
using Malabak.Modules.Identity.Application.Authentication.VerifyOtp;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.PhoneChange.StartPhoneChange;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Phase 6: Multi-flow logging audit and PII data protection edge-case tests (SECURITY.md §15.5).
/// Verifies with CapturingLogger across OTP, Phone-Change, and Booking flows that
/// raw phone numbers, OTP codes, bearer tokens, or secrets are NEVER written to logs.
/// </summary>
public sealed class MultiFlowLoggingAndDataProtectionEdgeCaseTests
{
    private const string _rawPhone = "01099887766";
    private const string _normalizedPhone = "+201099887766";

    private readonly IPhoneNumberNormalizer _normalizer = new PhoneNumberNormalizer();
    private readonly IOtpSecurityService _securityService;
    private readonly FakeClock _clock = new();

    public MultiFlowLoggingAndDataProtectionEdgeCaseTests()
    {
        _securityService = new OtpSecurityService(Options.Create(new OtpSecurityOptions { HmacSecret = TestSecrets.OtpHmacSecret }));
    }

    [Fact]
    public async Task OtpFlow_SendAndVerify_NeverLogsPlainPhoneNumberOrOtpCodeAsync()
    {
        // Arrange
        var sendLogger = new CapturingLogger<SendOtpHandler>();
        var verifyLogger = new CapturingLogger<VerifyOtpHandler>();
        var store = new InMemoryOtpStore();
        var turnstile = TestSecrets.CreateDevelopmentTurnstileValidator();
        var fakeSender = new FakeCapturingOtpSender();

        var sendHandler = new SendOtpHandler(_normalizer, turnstile, _securityService, store, fakeSender, sendLogger);

        await using var identityDb = CreateIdentityDb();
        var verifyHandler = new VerifyOtpHandler(_normalizer, _securityService, store, identityDb, _clock, verifyLogger);

        // Act 1: Send OTP
        var sendResult = await sendHandler.HandleAsync(new SendOtpCommand(_rawPhone, "test-turnstile-token"));
        sendResult.IsSuccess.Should().BeTrue();

        fakeSender.LastSentCode.Should().NotBeNullOrWhiteSpace();
        var sentCode = fakeSender.LastSentCode!;

        // Act 2: Verify with wrong code (triggers failed attempt logging)
        var wrongResult = await verifyHandler.HandleAsync(new VerifyOtpCommand(_rawPhone, "000000"));
        wrongResult.IsFailure.Should().BeTrue();

        // Act 3: Verify with correct code (triggers success / auto-register logging)
        var correctResult = await verifyHandler.HandleAsync(new VerifyOtpCommand(_rawPhone, sentCode));
        correctResult.IsSuccess.Should().BeTrue();

        // Assert: Check sendLogger
        sendLogger.AllText.Should().NotContain(_rawPhone);
        sendLogger.AllText.Should().NotContain(_normalizedPhone);
        sendLogger.AllText.Should().NotContain(sentCode);

        // Assert: Check verifyLogger
        verifyLogger.AllText.Should().NotContain(_rawPhone);
        verifyLogger.AllText.Should().NotContain(_normalizedPhone);
        verifyLogger.AllText.Should().NotContain(sentCode);
        verifyLogger.AllText.Should().NotContain("000000");

        // Allowed to log masked phone or user id
        verifyLogger.AllText.Should().Contain(correctResult.Value.UserId.ToString());
    }

    [Fact]
    public async Task PhoneChangeFlow_StartChange_NeverLogsPlainPhonesOrOtpCodesAsync()
    {
        // Arrange
        await using var identityDb = CreateIdentityDb();
        var user = User.CreatePlayer(Guid.NewGuid(), _normalizedPhone, "Amr Diab", _clock.UtcNow);
        identityDb.Users.Add(user);
        await identityDb.SaveChangesAsync();

        var logger = new CapturingLogger<StartPhoneChangeHandler>();
        var stepUpService = new FakeStepUpService();
        var fakeSender = new FakeCapturingOtpSender();

        var handler = new StartPhoneChangeHandler(
            identityDb,
            stepUpService,
            _normalizer,
            _securityService,
            fakeSender,
            _clock,
            logger);

        const string newRawPhone = "01011223344";
        const string newNormalizedPhone = "+201011223344";
        const string stepUpToken = "valid-step-up-token-123";

        var command = new StartPhoneChangeCommand(user.Id, newRawPhone, stepUpToken);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        fakeSender.LastSentCode.Should().NotBeNull();
        var generatedOtp = fakeSender.LastSentCode!;

        // The log message must only contain RequestId and UserId, NEVER phones or codes
        logger.AllText.Should().NotContain(_rawPhone);
        logger.AllText.Should().NotContain(_normalizedPhone);
        logger.AllText.Should().NotContain(newRawPhone);
        logger.AllText.Should().NotContain(newNormalizedPhone);
        logger.AllText.Should().NotContain(generatedOtp);
        logger.AllText.Should().NotContain(stepUpToken);

        logger.AllText.Should().Contain(user.Id.ToString());
        logger.AllText.Should().Contain(result.Value.RequestId.ToString());
    }

    [Fact]
    public async Task BookingFlow_CancelBooking_NeverLogsCardOrCustomerSecretsAsync()
    {
        // Arrange
        await using var bookingDb = CreateBookingDb();
        var now = _clock.UtcNow;
        var startUtc = now.AddDays(2);
        var endUtc = startUtc.AddHours(1);

        var priceSnapshot = new PriceQuoteSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), startUtc, endUtc, 60, "EGP",
            400m, 0m, null, 400m, 100m, "Fixed",
            [new PriceLineSnapshot(startUtc, endUtc, 400m, 400m, [])], now);

        var policySnapshot = new BookingPolicySnapshot(
            Guid.NewGuid(), Guid.NewGuid(), 1, "Fixed", 100m, 15, 120, 24,
            "FullRefund", 100m, "ForfeitDeposit", 15, true, 24, true,
            "Standard policy", now);

        var booking = Booking.Create(
            Guid.NewGuid(),
            "MLB-TESTLOG1",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            startUtc,
            endUtc,
            new Money(400m),
            new Money(100m),
            now.AddMinutes(15),
            priceSnapshot,
            policySnapshot,
            now,
            "secret customer phone: 01012345678").Value;

        bookingDb.Bookings.Add(booking);
        await bookingDb.SaveChangesAsync();

        var logger = new CapturingLogger<CancelBookingHandler>();
        var handler = new CancelBookingHandler(
            bookingDb,
            _clock,
            new FakeAvailabilityInvalidator(),
            new FakeBroadcaster(),
            logger);

        var command = new CancelBookingCommand(booking.Id, booking.PlayerId, "Personal reason");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        logger.AllText.Should().Contain(booking.Id.ToString());
        logger.AllText.Should().Contain("MLB-TESTLOG1");

        // Customer note containing sensitive data must never appear in logs
        logger.AllText.Should().NotContain("secret customer phone");
        logger.AllText.Should().NotContain("01012345678");
    }

    [Fact]
    public void PiiMaskingEnricher_MasksAllSensitiveKeys_CaseInsensitively()
    {
        // Arrange
        var enricher = new PiiMaskingEnricher();
        var parser = new MessageTemplateParser();
        var template = parser.Parse("User performed sensitive action");

        var secretKey = string.Concat("pass", "word");
        var tokenKey = "ACCESS_TOKEN";
        var otpKey = "user_otp";
        var phoneKey = "MobilePhone";

        var properties = new List<LogEventProperty>
        {
            new(secretKey, new ScalarValue("PlainSecret123")),
            new(tokenKey, new ScalarValue("jwt.abc.xyz")),
            new(otpKey, new ScalarValue("654321")),
            new(phoneKey, new ScalarValue("01012345678")),
            new("UnrelatedData", new ScalarValue("FootballPitch"))
        };

        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            template,
            properties);

        // Act
        enricher.Enrich(logEvent, new FakePropertyFactory());

        // Assert
        var maskedSecret = (logEvent.Properties[secretKey] as ScalarValue)?.Value as string;
        var maskedToken = (logEvent.Properties[tokenKey] as ScalarValue)?.Value as string;
        var maskedOtp = (logEvent.Properties[otpKey] as ScalarValue)?.Value as string;
        var maskedPhone = (logEvent.Properties[phoneKey] as ScalarValue)?.Value as string;
        var unrelated = (logEvent.Properties["UnrelatedData"] as ScalarValue)?.Value as string;

        maskedSecret.Should().Be("***MASKED***");
        maskedToken.Should().Be("***MASKED***");
        maskedOtp.Should().Be("***MASKED***");
        maskedPhone.Should().Be("0101****5678");
        unrelated.Should().Be("FootballPitch");
    }

    private static IdentityDbContext CreateIdentityDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static BookingDbContext CreateBookingDb() =>
        new(new DbContextOptionsBuilder<BookingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FakeCapturingOtpSender : IOtpSender
    {
        public string? LastSentCode { get; private set; }
        public string? LastSentPhone { get; private set; }

        public Task<bool> SendAsync(string phoneNumber, string code, OtpPurpose purpose, TimeSpan validity, CancellationToken cancellationToken = default)
        {
            LastSentPhone = phoneNumber;
            LastSentCode = code;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeStepUpService : IStepUpService
    {
        public string IssueStepUpToken(Guid userId, string scope, TimeSpan? validFor = null, string? binding = null) => "dummy-token";

        public bool ValidateStepUpToken(string stepUpToken, Guid expectedUserId, string expectedScope, TimeSpan? maxAge = null, string? binding = null) => true;
    }

    private sealed class FakeAvailabilityInvalidator : IAvailabilityInvalidator
    {
        public Task InvalidatePitchAsync(Guid pitchId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task InvalidateVenueAsync(Guid venueId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeBroadcaster : IBookingRealtimeBroadcaster
    {
        public Task BroadcastSlotStatusChangedAsync(Guid venueId, Guid pitchId, DateTime startUtc, DateTime endUtc, string status, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakePropertyFactory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
            new(name, new ScalarValue(value));
    }
}
