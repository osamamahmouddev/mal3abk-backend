using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.PhoneChange.RecoverPhone;

public sealed class RecoverPhoneHandler : ICommandHandler<RecoverPhoneCommand, Result>
{
    private static readonly TimeSpan _recoveryWindow = TimeSpan.FromHours(24);

    private readonly IdentityDbContext _dbContext;
    private readonly ITokenService _tokenService;
    private readonly ISmsAlertService _smsAlertService;
    private readonly IClock _clock;
    private readonly ILogger<RecoverPhoneHandler> _logger;

    public RecoverPhoneHandler(
        IdentityDbContext dbContext,
        ITokenService tokenService,
        ISmsAlertService smsAlertService,
        IClock clock,
        ILogger<RecoverPhoneHandler> logger)
    {
        _dbContext = dbContext;
        _tokenService = tokenService;
        _smsAlertService = smsAlertService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(
        RecoverPhoneCommand command,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var recoveryHash = _tokenService.HashRefreshToken(command.RecoveryToken);

        var request = await _dbContext.PhoneChangeRequests
            .FirstOrDefaultAsync(r => r.RecoveryTokenHash == recoveryHash, cancellationToken);

        if (request is null)
        {
            return Result.Failure(new Error(
                "Identity.InvalidRecoveryToken",
                "Recovery token is invalid or has already been used."));
        }

        if (request.Status != PhoneChangeStatus.Completed || !request.CompletedAtUtc.HasValue)
        {
            return Result.Failure(new Error(
                "Identity.InvalidRecoveryState",
                "Phone change was not completed."));
        }

        if (nowUtc - request.CompletedAtUtc.Value > _recoveryWindow)
        {
            return Result.Failure(new Error(
                "Identity.RecoveryTokenExpired",
                "The 24-hour recovery window has expired."));
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(new Error(
                "Identity.UserNotFound",
                "User not found."));
        }

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            // Revert phone number to old phone number and rotate security stamp
            user.ChangePhoneNumber(request.OldPhoneNumber, nowUtc);

            // Invalidate all active sessions
            var activeTokens = await _dbContext.RefreshTokens
                .Where(t => t.UserId == user.Id && !t.IsRevoked)
                .ToListAsync(cancellationToken);

            foreach (var token in activeTokens)
            {
                token.Revoke("phone_recovery_revert", nowUtc);
            }

            // Invalidate recovery token after use
            request.Complete(string.Empty, nowUtc);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await _smsAlertService.SendSecurityAlertAsync(
            request.OldPhoneNumber,
            "Your Malabak account phone number has been successfully restored. All remote sessions have been terminated.",
            cancellationToken);

        _logger.LogWarning(
            "Account phone recovery executed for user {UserId}. Restored phone number to {OldPhone}.",
            user.Id,
            request.OldPhoneNumber);

        return Result.Success();
    }
}
