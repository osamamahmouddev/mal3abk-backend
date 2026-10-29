namespace Malabak.SharedKernel.Locking;

/// <summary>
/// Represents an acquired distributed lock.
/// Releasing the lock ensures it is only released if the lock token still matches
/// the instance that acquired it (preventing accidental release after expiry).
/// </summary>
public interface IDistributedLock : IAsyncDisposable
{
    string Resource { get; }
    string LockToken { get; }
    bool IsAcquired { get; }
    DateTime AcquiredAtUtc { get; }
    TimeSpan Expiry { get; }

    Task<bool> ReleaseAsync(CancellationToken cancellationToken = default);
}
