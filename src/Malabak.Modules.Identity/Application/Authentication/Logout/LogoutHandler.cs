using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.Authentication.Logout;

public sealed class LogoutHandler : ICommandHandler<LogoutCommand, Result>
{
    private readonly IdentityDbContext _dbContext;
    private readonly ITokenService _tokenService;
    private readonly IClock _clock;
    private readonly ILogger<LogoutHandler> _logger;

    public LogoutHandler(
        IdentityDbContext dbContext,
        ITokenService tokenService,
        IClock clock,
        ILogger<LogoutHandler> logger)
    {
        _dbContext = dbContext;
        _tokenService = tokenService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(
        LogoutCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return Result.Success(); // Idempotent
        }

        var tokenHash = _tokenService.HashRefreshToken(command.RefreshToken);
        var token = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (token is not null && !token.IsRevoked)
        {
            token.Revoke("user_logout", _clock.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Refresh token {TokenId} revoked due to user logout", token.Id);
        }

        return Result.Success();
    }
}
