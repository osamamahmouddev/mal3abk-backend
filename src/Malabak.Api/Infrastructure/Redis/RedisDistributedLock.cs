using Malabak.SharedKernel.Locking;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Malabak.Api.Infrastructure.Redis;

/// <summary>
/// Implements a distributed lock backed by Redis with atomic token-checked release.
/// </summary>
public sealed class RedisDistributedLock : IDistributedLock
{
    private const string _releaseLockScript = @"
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end";

    private readonly IDatabase _database;
    private readonly ILogger _logger;
    private int _disposed;

    public string Resource { get; }
    public string LockToken { get; }
    public bool IsAcquired { get; private set; }
    public DateTime AcquiredAtUtc { get; }
    public TimeSpan Expiry { get; }

    public RedisDistributedLock(
        IDatabase database,
        string resource,
        string lockToken,
        TimeSpan expiry,
        DateTime acquiredAtUtc,
        ILogger logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        LockToken = lockToken ?? throw new ArgumentNullException(nameof(lockToken));
        Expiry = expiry;
        AcquiredAtUtc = acquiredAtUtc;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        IsAcquired = true;
    }

    public async Task<bool> ReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return false;
        }

        if (!IsAcquired)
        {
            return false;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = (int)await _database.ScriptEvaluateAsync(
                _releaseLockScript,
                new RedisKey[] { Resource },
                new RedisValue[] { LockToken }).ConfigureAwait(false);

            var released = result == 1;
            IsAcquired = false;

            if (released)
            {
                _logger.LogDebug("Successfully released distributed lock for resource '{Resource}'.", Resource);
            }
            else
            {
                _logger.LogWarning(
                    "Distributed lock for resource '{Resource}' was already expired or owned by another token upon release attempt.",
                    Resource);
            }

            return released;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release distributed lock for resource '{Resource}'.", Resource);
            IsAcquired = false;
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
