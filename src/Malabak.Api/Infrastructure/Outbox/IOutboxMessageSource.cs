namespace Malabak.Api.Infrastructure.Outbox;

public interface IOutboxMessageSource
{
    string SourceName { get; }
    Task<int> DispatchPendingMessagesAsync(int batchSize, CancellationToken cancellationToken = default);
}
