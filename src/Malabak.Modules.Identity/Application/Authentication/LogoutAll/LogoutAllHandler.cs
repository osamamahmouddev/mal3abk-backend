using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.Authentication.LogoutAll;

public sealed class LogoutAllHandler : ICommandHandler<LogoutAllCommand, Result>
{
    private readonly IdentityDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<LogoutAllHandler> _logger;
    private readonly SessionService? _sessions;

    public LogoutAllHandler(
        IdentityDbContext dbContext,
        IClock clock,
        ILogger<LogoutAllHandler> logger,
        SessionService? sessions = null)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
        _sessions = sessions;
    }

    public async Task<Result> HandleAsync(
        LogoutAllCommand command,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var activeTokens = await _dbContext.RefreshTokens
            .Where(t => t.UserId == command.UserId && !t.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.Revoke("user_logout_all", nowUtc);
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        user?.UpdateSecurityStamp(nowUtc);

        if (_sessions is not null)
        {
            await _sessions.RevokeAllAsync(command.UserId, exceptSessionId: null, "user_logout_all", cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Revoked {Count} active refresh tokens and updated security stamp for user {UserId}", activeTokens.Count, command.UserId);

        return Result.Success();
    }
}
