namespace Malabak.SharedKernel.Locking;

/// <summary>
/// Thrown when an attempt to acquire a distributed lock times out.
/// </summary>
public sealed class DistributedLockTimeoutException : TimeoutException
{
    public string Resource { get; }
    public TimeSpan Timeout { get; }

    public DistributedLockTimeoutException(string resource, TimeSpan timeout)
        : base($"Failed to acquire distributed lock for resource '{resource}' within {timeout.TotalMilliseconds}ms.")
    {
        Resource = resource;
        Timeout = timeout;
    }
}
