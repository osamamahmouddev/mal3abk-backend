namespace Malabak.SharedKernel.Locking;

/// <summary>
/// Provides distributed locking across modular monolith instances.
/// </summary>
public interface IDistributedLockProvider
{
    /// <summary>
    /// Attempts to acquire a distributed lock for the specified resource.
    /// Returns null if the lock could not be acquired within the specified timeout.
    /// </summary>
    Task<IDistributedLock?> TryAcquireLockAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan timeout = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires a distributed lock or throws <see cref="DistributedLockTimeoutException"/> if it cannot be acquired within the specified timeout.
    /// </summary>
    Task<IDistributedLock> AcquireLockAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan timeout = default,
        CancellationToken cancellationToken = default);
}
