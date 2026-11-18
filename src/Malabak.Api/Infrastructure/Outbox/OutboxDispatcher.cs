using System.Diagnostics;
using Malabak.Api.Infrastructure.Configuration;
using Malabak.SharedKernel.Caching;
using Malabak.SharedKernel.Locking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Malabak.Api.Infrastructure.Outbox;

public sealed class OutboxDispatcher : IOutboxDispatcher
{
    private readonly IEnumerable<IOutboxMessageSource> _sources;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IRedisKeyBuilder _keyBuilder;
    private readonly IOptions<OutboxOptions> _options;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        IEnumerable<IOutboxMessageSource> sources,
        IDistributedLockProvider lockProvider,
        IRedisKeyBuilder keyBuilder,
        IOptions<OutboxOptions> options,
        ILogger<OutboxDispatcher> logger)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _lockProvider = lockProvider ?? throw new ArgumentNullException(nameof(lockProvider));
        _keyBuilder = keyBuilder ?? throw new ArgumentNullException(nameof(keyBuilder));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> DispatchPendingMessagesAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Value.Enabled)
        {
            return 0;
        }

        var stopwatch = Stopwatch.StartNew();
        var totalDispatched = 0;

        foreach (var source in _sources)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (!await source.HasPendingMessagesAsync(cancellationToken))
            {
                continue;
            }

            var lockKey = _keyBuilder.Format("outbox", "lock", source.SourceName);
            var lockExpiry = TimeSpan.FromSeconds(_options.Value.DistributedLockTimeoutSeconds);

            var lockHandle = await _lockProvider.TryAcquireLockAsync(
                lockKey,
                lockExpiry,
                timeout: TimeSpan.Zero,
                cancellationToken: cancellationToken);

            if (lockHandle is null)
            {
                _logger.LogDebug("Another worker is currently processing outbox for {SourceName}. Skipping.", source.SourceName);
                continue;
            }

            await using (lockHandle)
            {
                try
                {
                    var dispatched = await source.DispatchPendingMessagesAsync(_options.Value.BatchSize, cancellationToken);
                    totalDispatched += dispatched;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error dispatching outbox messages from source {SourceName}", source.SourceName);
                }
            }
        }

        stopwatch.Stop();
        OutboxMetrics.RecordDuration(stopwatch.Elapsed.TotalMilliseconds);

        return totalDispatched;
    }
}
