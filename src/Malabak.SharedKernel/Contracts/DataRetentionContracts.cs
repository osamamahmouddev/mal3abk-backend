namespace Malabak.SharedKernel.Contracts;

/// <summary>
/// One kind of short-lived technical data a module keeps (spent tokens, delivered messages, delivery logs) that is removed
/// once it is no longer needed, so the database does not grow forever (ADR 0007). Never business, financial or audit data.
/// </summary>
public interface IDataRetentionTask
{
    /// <summary>A short name for the logs, e.g. "identity.refresh_tokens".</summary>
    string Name { get; }

    /// <summary>Hard-deletes what is past its retention at <paramref name="nowUtc"/>, in small batches; returns the rows removed.</summary>
    Task<int> PurgeAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>How long each kind of technical data is kept (ADR 0007).</summary>
public static class DataRetentionRules
{
    /// <summary>Rows deleted per statement, so a large backlog never locks a table for long.</summary>
    public const int BatchSize = 2_000;

    /// <summary>Refresh tokens are deleted this long after they expire (revoked or not); until then reuse is still detected.</summary>
    public static readonly TimeSpan RefreshTokensAfterExpiry = TimeSpan.FromDays(30);

    /// <summary>Signed-out or abandoned device sessions with no usable refresh token left.</summary>
    public static readonly TimeSpan InactiveSessions = TimeSpan.FromDays(90);

    /// <summary>SMS/WhatsApp delivery logs (the spend caps only look at the current month).</summary>
    public static readonly TimeSpan SmsDispatches = TimeSpan.FromDays(90);

    /// <summary>Domain-event outbox messages already delivered.</summary>
    public static readonly TimeSpan ProcessedOutbox = TimeSpan.FromDays(7);

    /// <summary>Inbox de-duplication records already handled (kept longer than the outbox so a late redelivery is still ignored).</summary>
    public static readonly TimeSpan ProcessedInbox = TimeSpan.FromDays(30);

    /// <summary>Push deliveries that were sent or failed for good.</summary>
    public static readonly TimeSpan FinishedPushDeliveries = TimeSpan.FromDays(30);
}
