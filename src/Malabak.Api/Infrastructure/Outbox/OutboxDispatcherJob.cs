using Hangfire;
using Malabak.Api.Infrastructure.Hangfire;
using Microsoft.Extensions.Logging;

namespace Malabak.Api.Infrastructure.Outbox;

public sealed class OutboxDispatcherJob
{
    private readonly IOutboxDispatcher _dispatcher;
    private readonly ILogger<OutboxDispatcherJob> _logger;

    public OutboxDispatcherJob(IOutboxDispatcher dispatcher, ILogger<OutboxDispatcherJob> logger)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Queue(HangfireQueues.Default)]
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var dispatched = await _dispatcher.DispatchPendingMessagesAsync(cancellationToken);
        if (dispatched > 0)
        {
            _logger.LogInformation("OutboxDispatcherJob dispatched {Count} messages.", dispatched);
        }
    }
}
