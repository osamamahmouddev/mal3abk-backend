using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Malabak.SharedKernel.Persistence;

public sealed class OutboxInterceptor : SaveChangesInterceptor
{
    private readonly IClock _clock;
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public OutboxInterceptor(IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ConvertDomainEventsToOutboxMessages(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ConvertDomainEventsToOutboxMessages(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ConvertDomainEventsToOutboxMessages(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var entitiesWithEvents = context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Select(entry => entry.Entity)
            .Where(entity => entity.DomainEvents.Count > 0)
            .ToList();

        if (entitiesWithEvents.Count == 0)
        {
            return;
        }

        var outboxSet = context.Set<OutboxMessage>();

        foreach (var entity in entitiesWithEvents)
        {
            var domainEvents = entity.DomainEvents.ToList();
            entity.ClearDomainEvents();

            foreach (var domainEvent in domainEvents)
            {
                var eventType = domainEvent.GetType().AssemblyQualifiedName ?? domainEvent.GetType().FullName!;
                var payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), _jsonOptions);

                var outboxMessage = new OutboxMessage
                {
                    Id = domainEvent.EventId != Guid.Empty ? domainEvent.EventId : IdGenerator.NewGuid(),
                    Type = eventType,
                    Content = payload,
                    OccurredOnUtc = domainEvent.OccurredOnUtc != default ? domainEvent.OccurredOnUtc : _clock.UtcNow
                };

                outboxSet.Add(outboxMessage);
            }
        }
    }
}
