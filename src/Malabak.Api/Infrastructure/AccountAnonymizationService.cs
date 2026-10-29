using Malabak.Modules.Identity.Application.Privacy;

namespace Malabak.Api.Infrastructure;

/// <summary>
/// Periodically erases personal data of accounts past the deletion retention window. Safe on several instances:
/// anonymization is idempotent. Pending TASK-105: may move to a Hangfire recurring job.
/// </summary>
public sealed class AccountAnonymizationService : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AccountAnonymizationService> _logger;

    public AccountAnonymizationService(IServiceScopeFactory scopeFactory, ILogger<AccountAnonymizationService> logger)
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
                var count = await scope.ServiceProvider.GetRequiredService<AccountAnonymizer>().RunAsync(stoppingToken);
                if (count > 0)
                {
                    _logger.LogInformation("Anonymized {Count} deleted accounts", count);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Account anonymization cycle failed");
            }
        }
    }
}
