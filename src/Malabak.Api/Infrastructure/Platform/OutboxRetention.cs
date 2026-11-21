using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Api.Infrastructure.Platform;

/// <summary>
/// A module's domain-event outbox and inbox: delivered outbox messages after a week, handled inbox records after 30 days.
/// Messages not delivered yet (or still retrying) are never touched.
/// </summary>
public sealed class OutboxRetention<TDbContext> : IDataRetentionTask
    where TDbContext : BaseDbContext
{
    private readonly TDbContext _dbContext;

    public OutboxRetention(TDbContext dbContext) => _dbContext = dbContext;

    public string Name => $"{typeof(TDbContext).Name}.outbox";

    public async Task<int> PurgeAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var outboxCutoff = nowUtc - DataRetentionRules.ProcessedOutbox;
        var inboxCutoff = nowUtc - DataRetentionRules.ProcessedInbox;

        var outbox = await _dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc != null && m.ProcessedOnUtc < outboxCutoff)
            .OrderBy(m => m.ProcessedOnUtc)
            .DeleteInBatchesAsync(cancellationToken);

        var inbox = await _dbContext.InboxMessages
            .Where(m => m.ProcessedOnUtc != null && m.ProcessedOnUtc < inboxCutoff)
            .OrderBy(m => m.ProcessedOnUtc)
            .DeleteInBatchesAsync(cancellationToken);

        return outbox + inbox;
    }
}
