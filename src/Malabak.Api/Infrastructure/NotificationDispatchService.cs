using Malabak.Modules.Notifications.Application;

namespace Malabak.Api.Infrastructure;

/// <summary>
/// Polls the notifications outbox. Safe to run on several instances: delivery is idempotent by dedupe key.
/// Pending TASK-105: may move to a Hangfire recurring job alongside the other background work.
/// </summary>
public sealed class NotificationDispatchService : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationDispatchService> _logger;

    public NotificationDispatchService(IServiceScopeFactory scopeFactory, ILogger<NotificationDispatchService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().ProcessPendingAsync(cancellationToken: stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Database unavailable etc.: try again on the next tick rather than crashing the host.
                _logger.LogWarning(ex, "Notification dispatch cycle failed");
            }
        }
    }
}
