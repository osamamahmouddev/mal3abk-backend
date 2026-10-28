using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Malabak.SharedKernel.Events;

public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DomainEventDispatcher>? _logger;

    public DomainEventDispatcher(IServiceProvider serviceProvider, ILogger<DomainEventDispatcher>? logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger;
    }

    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var eventType = domainEvent.GetType();
        var handlerInterfaceType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var handlers = _serviceProvider.GetServices(handlerInterfaceType);

        var handleMethod = handlerInterfaceType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync));
        if (handleMethod is null)
        {
            return;
        }

        foreach (var handler in handlers)
        {
            if (handler is null)
            {
                continue;
            }

            try
            {
                var task = (Task?)handleMethod.Invoke(handler, [domainEvent, cancellationToken]);
                if (task is not null)
                {
                    await task;
                }
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                _logger?.LogError(ex.InnerException,
                    "Failed executing domain event handler {HandlerType} for event {EventType} (EventId: {EventId})",
                    handler.GetType().Name, eventType.Name, domainEvent.EventId);
                throw ex.InnerException;
            }
        }
    }

    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        foreach (var domainEvent in domainEvents)
        {
            await DispatchAsync(domainEvent, cancellationToken);
        }
    }
}
