namespace Malabak.SharedKernel.Events;

public interface IInboxStore
{
    Task<bool> HasBeenProcessedAsync(Guid eventId, string handlerName, CancellationToken cancellationToken = default);
    Task RecordHandledAsync(Guid eventId, string handlerName, CancellationToken cancellationToken = default);
    Task RecordFailedAsync(Guid eventId, string handlerName, string error, CancellationToken cancellationToken = default);
}
