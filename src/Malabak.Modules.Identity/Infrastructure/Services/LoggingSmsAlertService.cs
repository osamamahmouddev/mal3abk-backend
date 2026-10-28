using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Infrastructure.Services;

public sealed class LoggingSmsAlertService : ISmsAlertService
{
    private readonly ILogger<LoggingSmsAlertService> _logger;

    public LoggingSmsAlertService(ILogger<LoggingSmsAlertService> logger)
    {
        _logger = logger;
    }

    public Task SendSecurityAlertAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "SMS Security Alert dispatched to phone {PhoneNumber}: {Message}",
            phoneNumber,
            message);

        return Task.CompletedTask;
    }
}
