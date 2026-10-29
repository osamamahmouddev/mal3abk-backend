using Malabak.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;

namespace Malabak.SharedKernel.Persistence;

public class EfInboxStore<TDbContext> : IInboxStore
    where TDbContext : BaseDbContext
{
    private readonly TDbContext _dbContext;
    private readonly IClock _clock;

    public EfInboxStore(TDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<bool> HasBeenProcessedAsync(Guid eventId, string handlerName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerName);

        return await _dbContext.InboxMessages
            .AnyAsync(m => m.EventId == eventId && m.HandlerName == handlerName && m.ProcessedOnUtc != null, cancellationToken);
    }

    public async Task RecordHandledAsync(Guid eventId, string handlerName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerName);

        var existing = await _dbContext.InboxMessages
            .FirstOrDefaultAsync(m => m.EventId == eventId && m.HandlerName == handlerName, cancellationToken);

        if (existing is not null)
        {
            existing.MarkAsProcessed(_clock.UtcNow);
        }
        else
        {
            var inboxMessage = new InboxMessage
            {
                EventId = eventId,
                HandlerName = handlerName,
                ReceivedOnUtc = _clock.UtcNow
            };
            inboxMessage.MarkAsProcessed(_clock.UtcNow);
            _dbContext.InboxMessages.Add(inboxMessage);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordFailedAsync(Guid eventId, string handlerName, string error, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerName);

        var existing = await _dbContext.InboxMessages
            .FirstOrDefaultAsync(m => m.EventId == eventId && m.HandlerName == handlerName, cancellationToken);

        if (existing is not null)
        {
            existing.MarkAsFailed(error);
        }
        else
        {
            var inboxMessage = new InboxMessage
            {
                EventId = eventId,
                HandlerName = handlerName,
                ReceivedOnUtc = _clock.UtcNow
            };
            inboxMessage.MarkAsFailed(error);
            _dbContext.InboxMessages.Add(inboxMessage);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

public class EfInboxStore : EfInboxStore<BaseDbContext>
{
    public EfInboxStore(BaseDbContext dbContext, IClock clock)
        : base(dbContext, clock)
    {
    }
}
