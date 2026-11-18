using System.Collections.Concurrent;
using System.Security.Cryptography;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Locking;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Malabak.Api.Infrastructure.Redis;

/// <summary>
/// Distributed lock provider using Redis with Fail-Closed semantics, random token verification, and retry backoff.
/// </summary>
public sealed class RedisDistributedLockProvider : IDistributedLockProvider
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _memoryLocks = new();
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IClock _clock;
    private readonly ILogger<RedisDistributedLockProvider> _logger;
    private readonly IHostEnvironment? _environment;

    public RedisDistributedLockProvider(
        IConnectionMultiplexer multiplexer,
        IClock clock,
        ILogger<RedisDistributedLockProvider> logger,
        IHostEnvironment? environment = null)
    {
        _multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _environment = environment;
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

        try
        {
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
        catch (RedisFailClosedException ex) when (_environment is null || _environment.IsDevelopment())
        {
            _logger.LogWarning(ex, "Redis distributed lock failed for resource '{Resource}'. Using in-memory fallback lock in development environment.", resource);
            return await AcquireInMemoryLockAsync(resource, expiry, timeout, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IDistributedLock?> AcquireInMemoryLockAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var sem = _memoryLocks.GetOrAdd(resource, _ => new SemaphoreSlim(1, 1));
        var timeoutMs = timeout <= TimeSpan.Zero ? 0 : (int)Math.Min(int.MaxValue, timeout.TotalMilliseconds);
        var acquired = await sem.WaitAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
        if (!acquired)
        {
            return null;
        }

        return new InMemoryDistributedLock(resource, sem, _clock.UtcNow, expiry, _logger);
    }

    private sealed class InMemoryDistributedLock : IDistributedLock
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly ILogger _logger;
        private int _released;

        public InMemoryDistributedLock(
            string resource,
            SemaphoreSlim semaphore,
            DateTime acquiredAtUtc,
            TimeSpan expiry,
            ILogger logger)
        {
            Resource = resource;
            _semaphore = semaphore;
            AcquiredAtUtc = acquiredAtUtc;
            Expiry = expiry;
            _logger = logger;
            LockToken = Guid.CreateVersion7().ToString("N");
        }

        public string Resource { get; }
        public string LockToken { get; }
        public bool IsAcquired => _released == 0;
        public DateTime AcquiredAtUtc { get; }
        public TimeSpan Expiry { get; }

        public Task<bool> ReleaseAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                try
                {
                    _semaphore.Release();
                    _logger.LogDebug("Released in-memory lock for resource '{Resource}'.", Resource);
                    return Task.FromResult(true);
                }
                catch (ObjectDisposedException)
                {
                    return Task.FromResult(false);
                }
            }

            return Task.FromResult(false);
        }

        public async ValueTask DisposeAsync()
        {
            await ReleaseAsync().ConfigureAwait(false);
        }
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
