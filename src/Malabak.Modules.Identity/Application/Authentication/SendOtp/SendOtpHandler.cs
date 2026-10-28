using Malabak.Modules.Identity.Application.Common;
using Malabak.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.Authentication.SendOtp;

public sealed class SendOtpHandler : ICommandHandler<SendOtpCommand, Result<SendOtpResponse>>
{
    private readonly IPhoneNumberNormalizer _normalizer;
    private readonly ITurnstileValidator _turnstileValidator;
    private readonly IOtpSecurityService _securityService;
    private readonly IOtpStore _otpStore;
    private readonly ILogger<SendOtpHandler> _logger;

    public SendOtpHandler(
        IPhoneNumberNormalizer normalizer,
        ITurnstileValidator turnstileValidator,
        IOtpSecurityService securityService,
        IOtpStore otpStore,
        ILogger<SendOtpHandler> logger)
    {
        _normalizer = normalizer;
        _turnstileValidator = turnstileValidator;
        _securityService = securityService;
        _otpStore = otpStore;
        _logger = logger;
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
        var isAllowed = await _otpStore.CheckAndRecordSendAttemptAsync(
            normalizedPhone,
            maxAttempts: 3,
            window: TimeSpan.FromMinutes(10),
            cancellationToken);

        if (!isAllowed)
        {
            return Result.Failure<SendOtpResponse>(new Error(
                "Identity.RateLimitExceeded",
                "Too many OTP requests. Please wait 10 minutes before requesting a new code."));
        }

        // 4. Generate 6-digit CSPRNG code and compute HMAC-SHA256
        var code = _securityService.GenerateSecureOtpCode();
        var hmacHash = _securityService.ComputeHmacHash(code);

        // 5. Store in Redis with 5-minute TTL and 5 verification attempts limit
        const int ttlMinutes = 5;
        await _otpStore.SaveOtpAsync(
            normalizedPhone,
            hmacHash,
            TimeSpan.FromMinutes(ttlMinutes),
            maxVerificationAttempts: 5,
            cancellationToken);

        _logger.LogInformation("Generated OTP for {Phone} with 5-minute validity (Code: {Code})", normalizedPhone, code);

        return Result.Success(new SendOtpResponse(
            normalizedPhone,
            ttlMinutes * 60,
            "Verification code sent successfully."));
    }
}
