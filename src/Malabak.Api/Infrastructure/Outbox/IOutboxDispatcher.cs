namespace Malabak.Api.Infrastructure.Outbox;

public interface IOutboxDispatcher
{
    Task<int> DispatchPendingMessagesAsync(CancellationToken cancellationToken = default);
}
