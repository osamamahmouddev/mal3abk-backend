using Hangfire;
using Malabak.Api.Infrastructure.Hangfire;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;

namespace Malabak.Api.Infrastructure.Platform;

/// <summary>
/// Once a night, removes the technical data every module keeps only for a while (ADR 0007): spent refresh tokens, dead
/// sessions, delivered outbox messages, delivery logs. One task failing does not stop the others; it runs again tomorrow.
/// </summary>
public sealed class DataRetentionJob
{
    public const string RecurringJobId = "platform-data-retention";
    public const string CronSchedule = "15 3 * * *";

    private readonly IEnumerable<IDataRetentionTask> _tasks;
    private readonly IClock _clock;
    private readonly ILogger<DataRetentionJob> _logger;

    public DataRetentionJob(IEnumerable<IDataRetentionTask> tasks, IClock clock, ILogger<DataRetentionJob> logger)
    {
        _tasks = tasks;
        _clock = clock;
        _logger = logger;
    }

    [Queue(HangfireQueues.Low)]
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var nowUtc = _clock.UtcNow;
        foreach (var task in _tasks)
        {
            try
            {
                var purged = await task.PurgeAsync(nowUtc, cancellationToken);
                if (purged > 0)
                {
                    _logger.LogInformation("Data retention: purged {Count} row(s) from {Task}", purged, task.Name);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Data retention: {Task} failed", task.Name);
            }
        }
    }
}
