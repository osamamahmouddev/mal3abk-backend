using Malabak.Modules.Identity.Application.Authentication.VerifyOtp;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.Authentication.FirebaseAuth;

public sealed class FirebaseAuthHandler : ICommandHandler<FirebaseAuthCommand, Result<VerifyOtpResponse>>
{
    private readonly IFirebaseAuthService _firebaseAuth;
    private readonly IPhoneNumberNormalizer _normalizer;
    private readonly IdentityDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<FirebaseAuthHandler> _logger;
    private readonly ITokenService? _tokenService;
    private readonly SessionService? _sessionService;

    public FirebaseAuthHandler(
        IFirebaseAuthService firebaseAuth,
        IPhoneNumberNormalizer normalizer,
        IdentityDbContext dbContext,
        IClock clock,
        ILogger<FirebaseAuthHandler> logger,
        ITokenService? tokenService = null,
        SessionService? sessionService = null)
    {
        _firebaseAuth = firebaseAuth;
        _normalizer = normalizer;
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
        _tokenService = tokenService;
        _sessionService = sessionService;
    }

    public async Task<Result<VerifyOtpResponse>> HandleAsync(
        FirebaseAuthCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Verify Firebase ID Token
        var verifyResult = await _firebaseAuth.VerifyIdTokenAsync(command.IdToken, cancellationToken);
        if (verifyResult.IsFailure)
        {
            return Result.Failure<VerifyOtpResponse>(verifyResult.Error);
        }

        var claims = verifyResult.Value;

        // 2. Normalize verified phone number
        var normalizeResult = _normalizer.Normalize(claims.PhoneNumber);
        if (normalizeResult.IsFailure)
        {
            return Result.Failure<VerifyOtpResponse>(normalizeResult.Error);
        }

        var normalizedPhone = normalizeResult.Value;
        var nowUtc = _clock.UtcNow;

        // 3. User Lookup or Auto-Registration for Players
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.PhoneNumber == normalizedPhone, cancellationToken);

        // Account pending deletion / restore check
        if (user is { IsDeleted: true })
        {
            if (!command.RestoreAccount || !user.CanRestore(nowUtc))
            {
                return Result.Failure<VerifyOtpResponse>(new Error(
                    "Identity.AccountPendingDeletion",
                    "This account is scheduled for deletion. Please confirm restoring the account to keep it."));
            }

            user.Restore(nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("AUDIT: account {UserId} restored via Firebase Auth", user.Id);
        }

        if (user is { IsSuspended: true })
        {
            _logger.LogWarning("Sign-in refused for suspended account {UserId}", user.Id);
            return Result.Failure<VerifyOtpResponse>(new Error("Identity.AccountSuspended", "This account is suspended. Contact support."));
        }

        var isNewUser = false;
        if (user is null)
        {
            string fullName;
            if (!string.IsNullOrWhiteSpace(command.FullName))
            {
                fullName = command.FullName.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(claims.Name))
            {
                fullName = claims.Name.Trim();
            }
            else
            {
                fullName = "Player";
            }

            user = User.CreatePlayer(
                id: Guid.NewGuid(),
                phoneNumber: normalizedPhone,
                fullName: fullName,
                createdAtUtc: nowUtc);

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync(cancellationToken);
            isNewUser = true;

            _logger.LogInformation("Auto-registered new player user {UserId} via Firebase Auth", user.Id);
        }

        // 4. Token Generation (ES256 Access Token + Rotating Refresh Token)
        string? accessToken = null;
        int? expiresIn = null;
        string? rawRefreshToken = null;
        DateTime? refreshTokenExpiresAtUtc = null;

        if (_tokenService is not null)
        {
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
