namespace Malabak.Api.Infrastructure.Platform;

/// <summary>Purges, once a day, whatever has been in the admin trash for longer than the retention (30 days).</summary>
public sealed class TrashPurgeJob
{
    public const string RecurringJobId = "admin-trash-purge-expired";
    public const string CronSchedule = "30 2 * * *";

    private readonly TrashService _trash;
    private readonly ILogger<TrashPurgeJob> _logger;

    public TrashPurgeJob(TrashService trash, ILogger<TrashPurgeJob> logger)
    {
        _trash = trash;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var result = await _trash.PurgeExpiredAsync(cancellationToken);
        if (result.Purged > 0 || result.Failed > 0)
        {
            _logger.LogInformation("Trash retention: purged {Purged} item(s), {Failed} failed", result.Purged, result.Failed);
        }
    }
}
