using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.Authentication.VerifyOtp;

public sealed class VerifyOtpHandler : ICommandHandler<VerifyOtpCommand, Result<VerifyOtpResponse>>
{
    private readonly IPhoneNumberNormalizer _normalizer;
    private readonly IOtpSecurityService _securityService;
    private readonly IOtpStore _otpStore;
    private readonly IdentityDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<VerifyOtpHandler> _logger;

    public VerifyOtpHandler(
        IPhoneNumberNormalizer normalizer,
        IOtpSecurityService securityService,
        IOtpStore otpStore,
        IdentityDbContext dbContext,
        IClock clock,
        ILogger<VerifyOtpHandler> logger)
    {
        _normalizer = normalizer;
        _securityService = securityService;
        _otpStore = otpStore;
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<VerifyOtpResponse>> HandleAsync(
        VerifyOtpCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Normalize phone number
        var normalizeResult = _normalizer.Normalize(command.PhoneNumber);
        if (normalizeResult.IsFailure)
        {
            return Result.Failure<VerifyOtpResponse>(normalizeResult.Error);
        }

        var normalizedPhone = normalizeResult.Value;

        // 2. Verify code using constant-time comparison in OTP store with 5-attempt limit
        var verificationResult = await _otpStore.VerifyAndConsumeOtpAsync(
            normalizedPhone,
            command.Code,
            _securityService,
            cancellationToken);

        if (verificationResult.IsExpiredOrNotFound)
        {
            return Result.Failure<VerifyOtpResponse>(new Error(
                "Identity.OtpExpiredOrNotFound",
                "Verification code has expired or does not exist. Please request a new code."));
        }

        if (verificationResult.IsMaxAttemptsExceeded)
        {
            _logger.LogWarning("Max OTP verification attempts exceeded for phone {Phone}", normalizedPhone);
            return Result.Failure<VerifyOtpResponse>(new Error(
                "Identity.OtpMaxAttemptsExceeded",
                "Maximum verification attempts exceeded. Please request a new code."));
        }

        if (!verificationResult.IsSuccess)
        {
            return Result.Failure<VerifyOtpResponse>(new Error(
                "Identity.InvalidOtpCode",
                $"Invalid verification code. You have {verificationResult.RemainingAttempts} attempts remaining."));
        }

        // 3. User Lookup or Auto-Registration for Players
        var nowUtc = _clock.UtcNow;
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.PhoneNumber == normalizedPhone, cancellationToken);

        var isNewUser = false;
        if (user is null)
        {
            var fullName = !string.IsNullOrWhiteSpace(command.FullName)
                ? command.FullName.Trim()
                : "Player";

            user = User.CreatePlayer(
                id: Guid.NewGuid(),
                phoneNumber: normalizedPhone,
                fullName: fullName,
                createdAtUtc: nowUtc);

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync(cancellationToken);
            isNewUser = true;

            _logger.LogInformation("Auto-registered new player user {UserId} with phone {Phone}", user.Id, normalizedPhone);
        }

        return Result.Success(new VerifyOtpResponse(
            user.Id,
            user.PhoneNumber,
            user.FullName,
            user.Roles,
            isNewUser));
    }
}
