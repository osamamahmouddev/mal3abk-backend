using System.Security.Cryptography;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Locking;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Malabak.Api.Infrastructure.Redis;

/// <summary>
/// Distributed lock provider using Redis with Fail-Closed semantics, random token verification, and retry backoff.
/// </summary>
public sealed class RedisDistributedLockProvider : IDistributedLockProvider
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IClock _clock;
    private readonly ILogger<RedisDistributedLockProvider> _logger;

    public RedisDistributedLockProvider(
        IConnectionMultiplexer multiplexer,
        IClock clock,
        ILogger<RedisDistributedLockProvider> logger)
    {
        _multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IDistributedLock?> TryAcquireLockAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan timeout = default,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            throw new ArgumentException("Lock resource name cannot be null or whitespace.", nameof(resource));
        }

        if (expiry <= TimeSpan.Zero)
        {
            throw new ArgumentException("Lock expiry must be greater than zero.", nameof(expiry));
        }

        var database = _multiplexer.GetDatabase();
        var lockToken = GenerateLockToken();
        var deadlineUtc = _clock.UtcNow.Add(timeout);

        return await FailClosedPolicy.ExecuteSensitiveAsync(
            "AcquireDistributedLock",
            resource,
            async () =>
            {
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var acquired = await database.StringSetAsync(
                        resource,
                        lockToken,
                        expiry,
                        When.NotExists).ConfigureAwait(false);

                    if (acquired)
                    {
                        _logger.LogDebug(
                            "Acquired distributed lock for resource '{Resource}' with expiry {ExpiryMs}ms.",
                            resource,
                            expiry.TotalMilliseconds);

                        return (IDistributedLock)new RedisDistributedLock(
                            database,
                            resource,
                            lockToken,
                            expiry,
                            _clock.UtcNow,
                            _logger);
                    }

                    if (timeout <= TimeSpan.Zero || _clock.UtcNow >= deadlineUtc)
                    {
                        break;
                    }

                    var remaining = deadlineUtc - _clock.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    var jitterMs = RandomNumberGenerator.GetInt32(10, 30);
                    var delayMs = Math.Min(50 + jitterMs, (int)remaining.TotalMilliseconds);

                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    }
                }
                while (_clock.UtcNow < deadlineUtc);

                _logger.LogDebug(
                    "Could not acquire distributed lock for resource '{Resource}' within timeout {TimeoutMs}ms.",
                    resource,
                    timeout.TotalMilliseconds);

                return null;
            },
            _logger,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IDistributedLock> AcquireLockAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan timeout = default,
        CancellationToken cancellationToken = default)
    {
        var @lock = await TryAcquireLockAsync(resource, expiry, timeout, cancellationToken).ConfigureAwait(false);

        if (@lock is null)
        {
            throw new DistributedLockTimeoutException(resource, timeout);
        }

        return @lock;
    }

    private static string GenerateLockToken()
    {
        return Guid.CreateVersion7().ToString("N");
    }
}
