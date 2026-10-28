using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.Authentication.RefreshToken;

public sealed class RefreshTokenHandler : ICommandHandler<RefreshTokenCommand, Result<RefreshTokenResponse>>
{
    private readonly IdentityDbContext _dbContext;
    private readonly ITokenService _tokenService;
    private readonly IClock _clock;
    private readonly ILogger<RefreshTokenHandler> _logger;

    public RefreshTokenHandler(
        IdentityDbContext dbContext,
        ITokenService tokenService,
        IClock clock,
        ILogger<RefreshTokenHandler> logger)
    {
        _dbContext = dbContext;
        _tokenService = tokenService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<RefreshTokenResponse>> HandleAsync(
        RefreshTokenCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return Result.Failure<RefreshTokenResponse>(new Error(
                "Identity.InvalidRefreshToken",
                "Refresh token must be provided."));
        }

        var tokenHash = _tokenService.HashRefreshToken(command.RefreshToken);
        var nowUtc = _clock.UtcNow;

        var token = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (token is null)
        {
            return Result.Failure<RefreshTokenResponse>(new Error(
                "Identity.InvalidRefreshToken",
                "The specified refresh token is invalid."));
        }

        // Reuse Detection: A revoked token was presented!
        if (token.IsRevoked)
        {
            var familyTokens = await _dbContext.RefreshTokens
                .Where(t => t.FamilyId == token.FamilyId && !t.IsRevoked)
                .ToListAsync(cancellationToken);

            foreach (var familyToken in familyTokens)
            {
                familyToken.Revoke("compromised_family_revocation", nowUtc);
            }

            var compromisedUser = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == token.UserId, cancellationToken);
            compromisedUser?.UpdateSecurityStamp(nowUtc);

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "SECURITY ALERT: Refresh token reuse detected for TokenId {TokenId}, FamilyId {FamilyId}, UserId {UserId}. Revoked {Count} active tokens in family.",
                token.Id, token.FamilyId, token.UserId, familyTokens.Count);

            return Result.Failure<RefreshTokenResponse>(new Error(
                "Identity.RefreshTokenReused",
                "Security alert: Token reuse detected. All sessions in this token chain have been revoked for your safety."));
        }

        // Expiration check
        if (token.IsExpired(nowUtc))
        {
            return Result.Failure<RefreshTokenResponse>(new Error(
                "Identity.RefreshTokenExpired",
                "The refresh token has expired. Please log in again."));
        }

        // Valid token: rotate
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == token.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<RefreshTokenResponse>(new Error(
                "Identity.UserNotFound",
                "User associated with this token was not found."));
        }

        var tokenResult = _tokenService.GenerateTokens(user);
        var newHash = _tokenService.HashRefreshToken(tokenResult.RefreshToken);

        token.Revoke(newHash, nowUtc);

        var newTokenEntity = Domain.RefreshToken.Create(
            id: Guid.NewGuid(),
            userId: user.Id,
            tokenHash: newHash,
            familyId: token.FamilyId,
            expiresAtUtc: tokenResult.RefreshTokenExpiresAtUtc,
            createdAtUtc: nowUtc);

        _dbContext.RefreshTokens.Add(newTokenEntity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new RefreshTokenResponse(
            AccessToken: tokenResult.AccessToken,
            ExpiresIn: tokenResult.ExpiresIn,
            RefreshToken: tokenResult.RefreshToken,
            RefreshTokenExpiresAtUtc: tokenResult.RefreshTokenExpiresAtUtc));
    }
}
