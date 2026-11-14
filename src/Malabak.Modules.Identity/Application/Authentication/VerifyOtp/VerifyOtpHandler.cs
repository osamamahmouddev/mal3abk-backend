using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.Authentication.VerifyOtp;

public sealed class VerifyOtpHandler : ICommandHandler<VerifyOtpCommand, Result<VerifyOtpResponse>>
{
    private readonly IPhoneNumberNormalizer _normalizer;
    private readonly IOtpSecurityService _securityService;
    private readonly IOtpStore _otpStore;
    private readonly IdentityDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ITokenService? _tokenService;
    private readonly SessionService? _sessionService;
    private readonly ILogger<VerifyOtpHandler> _logger;
    private readonly IHostEnvironment? _environment;

    public VerifyOtpHandler(
        IPhoneNumberNormalizer normalizer,
        IOtpSecurityService securityService,
        IOtpStore otpStore,
        IdentityDbContext dbContext,
        IClock clock,
        ILogger<VerifyOtpHandler> logger,
        ITokenService? tokenService = null,
        SessionService? sessionService = null,
        IHostEnvironment? environment = null)
    {
        _normalizer = normalizer;
        _securityService = securityService;
        _otpStore = otpStore;
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
        _tokenService = tokenService;
        _sessionService = sessionService;
        _environment = environment;
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
        // In Development/Testing, allow fixed bypass codes (123456 / 111111) for local mobile emulator testing.
        var isDevBypass = _environment != null &&
                          IdentityEnvironment.AllowsDevelopmentShortcuts(_environment) &&
                          (command.Code == "123456" || command.Code == "111111");

        OtpVerificationResult verificationResult;
        if (isDevBypass)
        {
            _logger.LogWarning("Bypassing OTP verification in Development mode for phone {MaskedPhone}", PhoneNumberMasker.Mask(normalizedPhone));
            verificationResult = new OtpVerificationResult(true, 5);
        }
        else
        {
            verificationResult = await _otpStore.VerifyAndConsumeOtpAsync(
                normalizedPhone,
                command.Code,
                _securityService,
                cancellationToken);
        }

        if (verificationResult.IsExpiredOrNotFound)
        {
            return Result.Failure<VerifyOtpResponse>(new Error(
                "Identity.OtpExpiredOrNotFound",
                "Verification code has expired or does not exist. Please request a new code."));
        }

        if (verificationResult.IsMaxAttemptsExceeded)
        {
            _logger.LogWarning("Max OTP verification attempts exceeded for phone {MaskedPhone}", PhoneNumberMasker.Mask(normalizedPhone));
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
        // The soft-delete filter would hide an account in its deletion grace period, and re-registering its
        // number would then violate the unique index, so deleted accounts are looked up too.
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.PhoneNumber == normalizedPhone, cancellationToken);

        // ADR-0007: during the 30-day grace period the owner of the number (proven by this OTP) may cancel the
        // deletion, but only with an explicit confirmation; otherwise the account stays closed and is never re-created.
        if (user is { IsDeleted: true })
        {
            if (!command.RestoreAccount || !user.CanRestore(nowUtc))
            {
                return Result.Failure<VerifyOtpResponse>(new Error(
                    "Identity.AccountPendingDeletion",
                    "This account is scheduled for deletion. Request a new code and confirm restoring the account to keep it."));
            }

            user.Restore(nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("AUDIT: account {UserId} restored during the deletion grace period", user.Id);
        }

        if (user is { IsSuspended: true })
        {
            _logger.LogWarning("Sign-in refused for suspended account {UserId}", user.Id);
            return Result.Failure<VerifyOtpResponse>(new Error("Identity.AccountSuspended", "This account is suspended. Contact support."));
        }

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

            _logger.LogInformation("Auto-registered new player user {UserId}", user.Id);
        }

        // 4. Token Generation (ES256 Access Token + Rotating Refresh Token)
        string? accessToken = null;
        int? expiresIn = null;
        string? rawRefreshToken = null;
        DateTime? refreshTokenExpiresAtUtc = null;

        if (_tokenService is not null)
        {
            // The session id doubles as the refresh-token family and the access token sid claim.
            var sessionId = _sessionService is not null
                ? await _sessionService.StartAsync(user, isAdminSession: false, cancellationToken)
                : Guid.NewGuid();
            var tokenResult = _tokenService.GenerateTokens(user, sessionId);
            accessToken = tokenResult.AccessToken;
            expiresIn = tokenResult.ExpiresIn;
            rawRefreshToken = tokenResult.RefreshToken;
            refreshTokenExpiresAtUtc = tokenResult.RefreshTokenExpiresAtUtc;

            var newHash = _tokenService.HashRefreshToken(tokenResult.RefreshToken);
            var initialFamilyId = sessionId;

            var refreshTokenEntity = Domain.RefreshToken.Create(
                id: Guid.NewGuid(),
                userId: user.Id,
                tokenHash: newHash,
                familyId: initialFamilyId,
                expiresAtUtc: tokenResult.RefreshTokenExpiresAtUtc,
                createdAtUtc: nowUtc);

            _dbContext.RefreshTokens.Add(refreshTokenEntity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new VerifyOtpResponse(
            user.Id,
            user.PhoneNumber,
            user.FullName,
            user.Roles,
            isNewUser,
            accessToken,
            expiresIn,
            rawRefreshToken,
            refreshTokenExpiresAtUtc));
    }
}
