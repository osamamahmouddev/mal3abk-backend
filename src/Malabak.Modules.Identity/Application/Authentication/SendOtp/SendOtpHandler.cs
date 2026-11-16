using Malabak.Modules.Identity.Application.Common;
using Malabak.SharedKernel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.Authentication.SendOtp;

public sealed class SendOtpHandler : ICommandHandler<SendOtpCommand, Result<SendOtpResponse>>
{
    private readonly IPhoneNumberNormalizer _normalizer;
    private readonly ITurnstileValidator _turnstileValidator;
    private readonly IOtpSecurityService _securityService;
    private readonly IOtpStore _otpStore;
    private readonly IOtpSender _otpSender;
    private readonly ILogger<SendOtpHandler> _logger;
    private readonly IHostEnvironment? _environment;

    public SendOtpHandler(
        IPhoneNumberNormalizer normalizer,
        ITurnstileValidator turnstileValidator,
        IOtpSecurityService securityService,
        IOtpStore otpStore,
        IOtpSender otpSender,
        ILogger<SendOtpHandler> logger,
        IHostEnvironment? environment = null)
    {
        _normalizer = normalizer;
        _turnstileValidator = turnstileValidator;
        _securityService = securityService;
        _otpStore = otpStore;
        _otpSender = otpSender;
        _logger = logger;
        _environment = environment;
    }

    public async Task<Result<SendOtpResponse>> HandleAsync(
        SendOtpCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Verify Cloudflare Turnstile token
        var isTurnstileValid = await _turnstileValidator.ValidateTokenAsync(
            command.TurnstileToken,
            command.RemoteIp,
            cancellationToken);

        if (!isTurnstileValid)
        {
            return Result.Failure<SendOtpResponse>(new Error(
                "Identity.InvalidTurnstileToken",
                "Anti-bot verification failed. Please try again."));
        }

        // 2. Validate & normalize Egyptian E.164 phone number
        var normalizeResult = _normalizer.Normalize(command.PhoneNumber);
        if (normalizeResult.IsFailure)
        {
            return Result.Failure<SendOtpResponse>(normalizeResult.Error);
        }

        var normalizedPhone = normalizeResult.Value;

        // 3. Enforce sliding window rate limit (max 3 requests per 10 minutes)
        var window = TimeSpan.FromMinutes(10);
        var isAllowed = await _otpStore.CheckAndRecordSendAttemptAsync(
            normalizedPhone,
            maxAttempts: 3,
            window: window,
            cancellationToken);

        if (!isAllowed)
        {
            // Tell the app exactly when the next code can be requested, so it can count down instead of guessing.
            var retryAfter = await _otpStore.GetSendRetryAfterAsync(normalizedPhone, window, cancellationToken);
            return Result.Failure<SendOtpResponse>(new Error(
                "Identity.RateLimitExceeded",
                "Too many OTP requests. Please wait before requesting a new code.")
            {
                RetryAfter = retryAfter,
            });
        }

        // 4. Generate 6-digit CSPRNG code and compute HMAC-SHA256
        var code = _securityService.GenerateSecureOtpCode();
        var hmacHash = _securityService.ComputeHmacHash(code);

        // 5. Store in Redis with 5-minute TTL and 5 verification attempts limit
        const int ttlMinutes = 5;
        var validity = TimeSpan.FromMinutes(ttlMinutes);
        await _otpStore.SaveOtpAsync(
            normalizedPhone,
            hmacHash,
            validity,
            maxVerificationAttempts: 5,
            cancellationToken);

        // 6. Deliver the code. Neither the code nor the phone number is logged (SECURITY.md §13).
        // App Store / Google Play review account uses fixed static verification code without external SMS delivery
        if (normalizedPhone == "+201000000999")
        {
            _logger.LogInformation("Reviewer test account OTP request simulated successfully for {MaskedPhone}", PhoneNumberMasker.Mask(normalizedPhone));
            return Result.Success(new SendOtpResponse(
                normalizedPhone,
                ttlMinutes * 60,
                $"Verification code sent to {PhoneNumberMasker.Mask(normalizedPhone)}. Valid for {ttlMinutes} minutes."));
        }

        if (!await _otpSender.SendAsync(normalizedPhone, code, OtpPurpose.Login, validity, cancellationToken))
        {
            return Result.Failure<SendOtpResponse>(OtpDeliveryErrors.DeliveryFailed);
        }

        if (_environment != null && IdentityEnvironment.AllowsDevelopmentShortcuts(_environment))
        {
            _logger.LogInformation("DEVELOPMENT OTP for {MaskedPhone} is: {Code}", PhoneNumberMasker.Mask(normalizedPhone), code);
        }
        else
        {
            _logger.LogInformation("Login OTP issued with {TtlMinutes}-minute validity.", ttlMinutes);
        }

        return Result.Success(new SendOtpResponse(
            normalizedPhone,
            ttlMinutes * 60,
            "Verification code sent successfully."));
    }
}
