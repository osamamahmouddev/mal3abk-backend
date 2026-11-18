namespace Malabak.Api.Infrastructure.Outbox;

public interface IOutboxMessageSource
{
    string SourceName { get; }
    Task<bool> HasPendingMessagesAsync(CancellationToken cancellationToken = default);
    Task<int> DispatchPendingMessagesAsync(int batchSize, CancellationToken cancellationToken = default);
}
