using FluentAssertions;
using Malabak.Modules.Identity.Application.Authentication.SendOtp;
using Malabak.Modules.Identity.Application.Authentication.VerifyOtp;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Malabak.UnitTests;

public sealed class OtpAuthTests
{
    private readonly IPhoneNumberNormalizer _normalizer = new PhoneNumberNormalizer();
    private readonly IOtpSecurityService _securityService;
    private readonly IConfiguration _configuration;

    public OtpAuthTests()
    {
        var configDict = new Dictionary<string, string?>
        {
            ["Security:Otp:HmacSecret"] = "TestSecretKeyForHmacSha256Authentication123456!",
            ["Security:Turnstile:BypassInDevelopment"] = "true"
        };

        _configuration = new TestConfiguration(configDict);
        _securityService = new OtpSecurityService(_configuration);
    }

    [Theory]
    [InlineData("01012345678", "+201012345678")]
    [InlineData("01123456789", "+201123456789")]
    [InlineData("01234567890", "+201234567890")]
    [InlineData("01512345678", "+201512345678")]
    [InlineData("+201012345678", "+201012345678")]
    public void PhoneNumberNormalizer_ShouldFormatValidEgyptianMobile_ToE164(string input, string expected)
    {
        var result = _normalizer.Normalize(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("+14155552671")] // US number
    [InlineData("0222345678")]  // Egyptian landline (02), not mobile
    public void PhoneNumberNormalizer_ShouldRejectInvalidOrNonEgyptianMobile(string input)
    {
        var result = _normalizer.Normalize(input);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void OtpSecurityService_GenerateSecureOtpCode_ShouldBe6Digits()
    {
        for (var i = 0; i < 50; i++)
        {
            var code = _securityService.GenerateSecureOtpCode();
            code.Should().HaveLength(6);
            code.Should().MatchRegex("^[1-9][0-9]{5}$");
        }
    }

    [Fact]
    public void OtpSecurityService_VerifyCode_ShouldUseConstantTimeAndReturnExpected()
    {
        var code = "482910";
        var hash = _securityService.ComputeHmacHash(code);

        _securityService.VerifyCode(code, hash).Should().BeTrue();
        _securityService.VerifyCode("482911", hash).Should().BeFalse();
        _securityService.VerifyCode("", hash).Should().BeFalse();
    }

    [Fact]
    public async Task OtpStore_SlidingWindowRateLimiting_ShouldLimitTo3AttemptsPer10MinutesAsync()
    {
        var store = new InMemoryOtpStore();
        var phone = "+201012345678";

        // 3 allowed
        (await store.CheckAndRecordSendAttemptAsync(phone)).Should().BeTrue();
        (await store.CheckAndRecordSendAttemptAsync(phone)).Should().BeTrue();
        (await store.CheckAndRecordSendAttemptAsync(phone)).Should().BeTrue();

        // 4th attempt rejected
        (await store.CheckAndRecordSendAttemptAsync(phone)).Should().BeFalse();
    }

    [Fact]
    public async Task OtpStore_VerificationAttempts_ShouldEnforce5AttemptLimitAsync()
    {
        var store = new InMemoryOtpStore();
        var phone = "+201099998888";
        var correctCode = "123456";
        var hash = _securityService.ComputeHmacHash(correctCode);

        await store.SaveOtpAsync(phone, hash, TimeSpan.FromMinutes(5), maxVerificationAttempts: 5);

        // 4 failed attempts
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var res = await store.VerifyAndConsumeOtpAsync(phone, "000000", _securityService);
            res.IsSuccess.Should().BeFalse();
            res.RemainingAttempts.Should().Be(5 - attempt);
        }

        // 5th attempt with wrong code reaches max limit
        var fifthFail = await store.VerifyAndConsumeOtpAsync(phone, "000000", _securityService);
        fifthFail.IsSuccess.Should().BeFalse();
        fifthFail.IsMaxAttemptsExceeded.Should().BeTrue();

        // Even with correct code after lock, it should fail
        var lockedAttempt = await store.VerifyAndConsumeOtpAsync(phone, correctCode, _securityService);
        lockedAttempt.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task SendOtpHandler_WhenValid_ShouldGenerateAndStoreOtpAsync()
    {
        var store = new InMemoryOtpStore();
        var turnstile = new TurnstileValidator(_configuration, NullLogger<TurnstileValidator>.Instance);
        var sender = new RecordingOtpSender();
        var handler = new SendOtpHandler(_normalizer, turnstile, _securityService, store, sender, NullLogger<SendOtpHandler>.Instance);

        var command = new SendOtpCommand("01011223344", "test-turnstile-token");
        var result = await handler.HandleAsync(command);

        result.IsSuccess.Should().BeTrue();
        result.Value.NormalizedPhoneNumber.Should().Be("+201011223344");
        result.Value.ExpiresInSeconds.Should().Be(300);

        // The delivered code is the one that verifies
        sender.LastRecipient.Should().Be("+201011223344");
        sender.LastPurpose.Should().Be(OtpPurpose.Login);
        sender.LastValidity.Should().Be(TimeSpan.FromMinutes(5));
        var verification = await store.VerifyAndConsumeOtpAsync("+201011223344", sender.LastCode, _securityService);
        verification.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SendOtpHandler_WhenDeliveryFails_ShouldReturnDeliveryFailedAsync()
    {
        var store = new InMemoryOtpStore();
        var turnstile = new TurnstileValidator(_configuration, NullLogger<TurnstileValidator>.Instance);
        var sender = new RecordingOtpSender { Succeeds = false };
        var handler = new SendOtpHandler(_normalizer, turnstile, _securityService, store, sender, NullLogger<SendOtpHandler>.Instance);

        var result = await handler.HandleAsync(new SendOtpCommand("01011223344", "test-turnstile-token"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(OtpDeliveryErrors.DeliveryFailedCode);
    }

    [Fact]
    public async Task SendOtpHandler_WhenTurnstileFails_ShouldReturnFailureAsync()
    {
        var store = new InMemoryOtpStore();
        var turnstile = new TurnstileValidator(_configuration, NullLogger<TurnstileValidator>.Instance);
        var handler = new SendOtpHandler(_normalizer, turnstile, _securityService, store, new RecordingOtpSender(), NullLogger<SendOtpHandler>.Instance);

        var command = new SendOtpCommand("01011223344", ""); // empty turnstile
        var result = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.InvalidTurnstileToken");
    }

    [Fact]
    public async Task SendOtpHandler_WhenRateLimitExceeded_ShouldReturnRateLimitErrorAsync()
    {
        var store = new InMemoryOtpStore();
        var turnstile = new TurnstileValidator(_configuration, NullLogger<TurnstileValidator>.Instance);
        var handler = new SendOtpHandler(_normalizer, turnstile, _securityService, store, new RecordingOtpSender(), NullLogger<SendOtpHandler>.Instance);

        var command = new SendOtpCommand("01011223344", "test-token");

        (await handler.HandleAsync(command)).IsSuccess.Should().BeTrue();
        (await handler.HandleAsync(command)).IsSuccess.Should().BeTrue();
        (await handler.HandleAsync(command)).IsSuccess.Should().BeTrue();

        var fourthResult = await handler.HandleAsync(command);
        fourthResult.IsFailure.Should().BeTrue();
        fourthResult.Error.Code.Should().Be("Identity.RateLimitExceeded");
    }

    [Fact]
    public void SendOtpValidator_ShouldValidateProperly()
    {
        var validator = new SendOtpValidator();

        var valid = validator.Validate(new SendOtpCommand("01012345678", "turnstile_token"));
        valid.IsValid.Should().BeTrue();

        var emptyPhone = validator.Validate(new SendOtpCommand("", "turnstile_token"));
        emptyPhone.IsValid.Should().BeFalse();

        var emptyToken = validator.Validate(new SendOtpCommand("01012345678", ""));
        emptyToken.IsValid.Should().BeFalse();
    }

    [Fact]
    public void VerifyOtpValidator_ShouldValidateProperly()
    {
        var validator = new VerifyOtpValidator();

        var valid = validator.Validate(new VerifyOtpCommand("01012345678", "123456"));
        valid.IsValid.Should().BeTrue();

        var shortCode = validator.Validate(new VerifyOtpCommand("01012345678", "12345"));
        shortCode.IsValid.Should().BeFalse();

        var alphaCode = validator.Validate(new VerifyOtpCommand("01012345678", "12345a"));
        alphaCode.IsValid.Should().BeFalse();
    }

    private sealed class TestConfiguration : IConfiguration
    {
        private readonly Dictionary<string, string?> _data;

        public TestConfiguration(Dictionary<string, string?> data)
        {
            _data = data;
        }

        public string? this[string key]
        {
            get => _data.TryGetValue(key, out var value) ? value : null;
            set => _data[key] = value;
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }

    private sealed class RecordingOtpSender : IOtpSender
    {
        public bool Succeeds { get; init; } = true;
        public string LastRecipient { get; private set; } = string.Empty;
        public string LastCode { get; private set; } = string.Empty;
        public OtpPurpose? LastPurpose { get; private set; }
        public TimeSpan LastValidity { get; private set; }

        public Task<bool> SendAsync(string phoneNumber, string code, OtpPurpose purpose, TimeSpan validity, CancellationToken cancellationToken = default)
        {
            LastRecipient = phoneNumber;
            LastCode = code;
            LastPurpose = purpose;
            LastValidity = validity;
            return Task.FromResult(Succeeds);
        }
    }
}
