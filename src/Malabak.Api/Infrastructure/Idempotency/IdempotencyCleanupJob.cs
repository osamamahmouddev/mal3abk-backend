using Hangfire;
using Malabak.Api.Infrastructure.Hangfire;
using Malabak.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Malabak.Api.Infrastructure.Idempotency;

public sealed class IdempotencyCleanupJob
{
    private readonly IIdempotencyStore _store;
    private readonly IClock _clock;
    private readonly ILogger<IdempotencyCleanupJob> _logger;

    public IdempotencyCleanupJob(
        IIdempotencyStore store,
        IClock clock,
        ILogger<IdempotencyCleanupJob> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Queue(HangfireQueues.Low)]
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var purgedCount = await _store.CleanupExpiredAsync(_clock.UtcNow, cancellationToken);
        if (purgedCount > 0)
        {
            _logger.LogInformation("IdempotencyCleanupJob purged {Count} expired idempotency records.", purgedCount);
        }
    }
}
