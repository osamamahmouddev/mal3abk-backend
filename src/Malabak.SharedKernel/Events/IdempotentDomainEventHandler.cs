using Microsoft.Extensions.Logging;

namespace Malabak.SharedKernel.Events;

public abstract class IdempotentDomainEventHandler<TEvent> : IDomainEventHandler<TEvent>
    where TEvent : IDomainEvent
{
    private readonly IInboxStore _inboxStore;
    private readonly ILogger _logger;

    public abstract string HandlerName { get; }

    protected IdempotentDomainEventHandler(IInboxStore inboxStore, ILogger logger)
    {
        _inboxStore = inboxStore ?? throw new ArgumentNullException(nameof(inboxStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var alreadyProcessed = await _inboxStore.HasBeenProcessedAsync(domainEvent.EventId, HandlerName, cancellationToken);
        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "Event {EventId} was already processed by {HandlerName}. Skipping duplicate execution.",
                domainEvent.EventId,
                HandlerName);
            return;
        }

        try
        {
            await HandleIdempotentAsync(domainEvent, cancellationToken);
            await _inboxStore.RecordHandledAsync(domainEvent.EventId, HandlerName, cancellationToken);
        }
        catch (Exception ex)
        {
            await _inboxStore.RecordFailedAsync(domainEvent.EventId, HandlerName, ex.Message, cancellationToken);
            throw;
        }
    }

    protected abstract Task HandleIdempotentAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
