using Malabak.Modules.Notifications.Domain;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Persistence;

namespace Malabak.Modules.Notifications.Infrastructure;

/// <summary>Notification outbox messages already turned into notifications, a week after they were processed.</summary>
public sealed class NotificationOutboxRetention : IDataRetentionTask
{
    private readonly NotificationsDbContext _dbContext;

    public NotificationOutboxRetention(NotificationsDbContext dbContext) => _dbContext = dbContext;

    public string Name => "notifications.outbox";

    public Task<int> PurgeAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var cutoff = nowUtc - DataRetentionRules.ProcessedOutbox;
        return _dbContext.Outbox
            .Where(m => m.ProcessedAtUtc != null && m.ProcessedAtUtc < cutoff)
            .OrderBy(m => m.ProcessedAtUtc)
            .DeleteInBatchesAsync(cancellationToken);
    }
}

/// <summary>Push deliveries that were sent or failed for good, 30 days on. Pending ones are never touched.</summary>
public sealed class PushDeliveryRetention : IDataRetentionTask
{
    private readonly NotificationsDbContext _dbContext;

    public PushDeliveryRetention(NotificationsDbContext dbContext) => _dbContext = dbContext;

    public string Name => "notifications.push_deliveries";

    public Task<int> PurgeAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var cutoff = nowUtc - DataRetentionRules.FinishedPushDeliveries;
        return _dbContext.PushDeliveries
            .Where(d => d.Status != PushStatus.Pending && d.CreatedAtUtc < cutoff)
            .OrderBy(d => d.CreatedAtUtc)
            .DeleteInBatchesAsync(cancellationToken);
    }
}
