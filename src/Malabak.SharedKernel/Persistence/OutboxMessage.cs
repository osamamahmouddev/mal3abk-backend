namespace Malabak.SharedKernel.Persistence;

public sealed class OutboxMessage : IOutboxMessage
{
    public Guid Id { get; init; } = IdGenerator.NewGuid();
    public string Type { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTime OccurredOnUtc { get; init; }
    public DateTime? ProcessedOnUtc { get; private set; }
    public string? Error { get; private set; }
    public int RetryCount { get; private set; }
    public DateTime? NextRetryUtc { get; private set; }
    public string? LockToken { get; private set; }
    public DateTime? LockedUntilUtc { get; private set; }

    public void MarkAsProcessed(DateTime processedOnUtc)
    {
        ProcessedOnUtc = processedOnUtc;
        Error = null;
        LockToken = null;
        LockedUntilUtc = null;
    }

    public void MarkAsFailed(string error, DateTime? nextRetryUtc)
    {
        RetryCount++;
        Error = error;
        NextRetryUtc = nextRetryUtc;
        LockToken = null;
        LockedUntilUtc = null;
    }

    public bool TryAcquireLock(string lockToken, DateTime lockedUntilUtc, DateTime currentUtc)
    {
        if (LockedUntilUtc.HasValue && LockedUntilUtc.Value > currentUtc)
        {
            return false;
        }

        LockToken = lockToken;
        LockedUntilUtc = lockedUntilUtc;
        return true;
    }

    public void ReleaseLock()
    {
        LockToken = null;
        LockedUntilUtc = null;
    }
}
