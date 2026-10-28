using Malabak.SharedKernel.Caching;
using Malabak.SharedKernel.Jobs;
using Malabak.SharedKernel.Locking;
using Microsoft.Extensions.Logging;

namespace Malabak.Api.Infrastructure.Hangfire;

/// <summary>
/// Base class for background jobs that guarantees idempotency across retries, restarts, and concurrent workers.
/// </summary>
/// <typeparam name="TArgs">The type representing job payload arguments.</typeparam>
public abstract class IdempotentBackgroundJob<TArgs> : IIdempotentJob<TArgs>
{
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IAtomicConsumeStore _atomicStore;
    private readonly ILogger _logger;

    protected virtual TimeSpan LockExpiry => TimeSpan.FromMinutes(10);
    protected virtual TimeSpan LockTimeout => TimeSpan.FromSeconds(5);
    protected virtual TimeSpan CompletedTtl => TimeSpan.FromDays(1);

    protected IdempotentBackgroundJob(
        IDistributedLockProvider lockProvider,
        IAtomicConsumeStore atomicStore,
        ILogger logger)
    {
        _lockProvider = lockProvider ?? throw new ArgumentNullException(nameof(lockProvider));
        _atomicStore = atomicStore ?? throw new ArgumentNullException(nameof(atomicStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(
        string idempotencyKey,
        TArgs args,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("IdempotencyKey cannot be null or whitespace.", nameof(idempotencyKey));
        }

        ArgumentNullException.ThrowIfNull(args);

        var completedKey = $"malabak:jobs:completed:{idempotencyKey.Trim()}";
        var lockResource = $"malabak:jobs:lock:{idempotencyKey.Trim()}";

        // Fast-path: check if job was already processed to completion
        if (await _atomicStore.ExistsAsync(completedKey, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "Idempotent job '{JobType}' with key '{IdempotencyKey}' was already completed. Skipping duplicate execution.",
                GetType().Name,
                idempotencyKey);
            return;
        }

        // Acquire distributed lock to prevent concurrent duplicate execution
        var @lock = await _lockProvider.TryAcquireLockAsync(
            lockResource,
            LockExpiry,
            LockTimeout,
            cancellationToken).ConfigureAwait(false);

        if (@lock is null)
        {
            _logger.LogWarning(
                "Could not acquire lock for job '{JobType}' with key '{IdempotencyKey}'. Another worker is currently processing this job.",
                GetType().Name,
                idempotencyKey);
            return;
        }

        await using (@lock.ConfigureAwait(false))
        {
            // Double-check completion status inside the lock
            if (await _atomicStore.ExistsAsync(completedKey, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation(
                    "Idempotent job '{JobType}' with key '{IdempotencyKey}' completed just before lock acquisition. Skipping.",
                    GetType().Name,
                    idempotencyKey);
                return;
            }

            _logger.LogInformation(
                "Starting execution of idempotent job '{JobType}' with key '{IdempotencyKey}'.",
                GetType().Name,
                idempotencyKey);

            await ExecuteCoreAsync(args, cancellationToken).ConfigureAwait(false);

            // Mark completed upon successful execution
            await _atomicStore.SetAsync(completedKey, "completed", CompletedTtl, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Successfully completed idempotent job '{JobType}' with key '{IdempotencyKey}'.",
                GetType().Name,
                idempotencyKey);
        }
    }

    /// <summary>
    /// Executes the domain logic of the background job.
    /// This method is only called if the job has not already run and the distributed lock has been acquired.
    /// </summary>
    protected abstract Task ExecuteCoreAsync(TArgs args, CancellationToken cancellationToken);
}
