using System.Text.Json;
using Malabak.Modules.Identity.Application.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// The database is the source of truth; Redis caches "active" for 60s and remembers revocations, so the per-request
/// check is usually one Redis lookup. If Redis is unavailable we fall back to the database rather than failing open.
/// Revocations are published on <see cref="RevokedChannel"/> for realtime hubs to disconnect those sessions.
/// </summary>
public sealed class RedisSessionStore : ISessionStore
{
    public const string RevokedChannel = "malabak:sessions:revoked";
    private static readonly TimeSpan _activeCacheTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan _revokedTtl = TimeSpan.FromDays(90);

    private readonly IdentityDbContext _dbContext;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<RedisSessionStore> _logger;

    public RedisSessionStore(IdentityDbContext dbContext, IServiceProvider services, ILogger<RedisSessionStore> logger)
    {
        _dbContext = dbContext;
        _redis = services.GetService<IConnectionMultiplexer>();
        _logger = logger;
    }

    public async Task<bool> IsActiveAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var db = RedisOrNull();
        if (db is not null)
        {
            try
            {
                if (await db.KeyExistsAsync(RevokedKey(sessionId)))
                {
                    return false;
                }

                if (await db.KeyExistsAsync(ActiveKey(sessionId)))
                {
                    return true;
                }
            }
            catch (RedisException ex)
            {
                _logger.LogWarning(ex, "Session cache unavailable; checking the database");
                db = null;
            }
        }

        var active = await _dbContext.UserSessions.AsNoTracking().AnyAsync(s => s.Id == sessionId && !s.IsRevoked, cancellationToken);
        if (active && db is not null)
        {
            await TryAsync(() => db.StringSetAsync(ActiveKey(sessionId), "1", _activeCacheTtl));
        }

        return active;
    }

    public async Task MarkRevokedAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken = default)
    {
        var db = RedisOrNull();
        if (db is null)
        {
            return;
        }

        foreach (var sessionId in sessionIds)
        {
            await TryAsync(() => db.KeyDeleteAsync(ActiveKey(sessionId)));
            await TryAsync(() => db.StringSetAsync(RevokedKey(sessionId), "1", _revokedTtl));
            await TryAsync(() => _redis!.GetSubscriber().PublishAsync(
                RedisChannel.Literal(RevokedChannel), JsonSerializer.Serialize(new { sessionId })));
        }
    }

    private IDatabase? RedisOrNull() => _redis is { IsConnected: true } ? _redis.GetDatabase() : null;

    private async Task TryAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (RedisException ex)
        {
            // The database already holds the revocation; the cache entry expires within 60s at worst.
            _logger.LogWarning(ex, "Session cache update failed");
        }
    }

    private static string ActiveKey(Guid id) => $"session:active:{id:N}";

    private static string RevokedKey(Guid id) => $"session:revoked:{id:N}";
}
