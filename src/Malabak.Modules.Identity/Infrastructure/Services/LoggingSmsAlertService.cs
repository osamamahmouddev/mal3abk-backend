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
        // The body can carry OTP codes or recovery links, so it is never logged (SECURITY.md §15.5).
        // Real delivery comes with the SMS provider abstraction (TASK-203).
        _logger.LogInformation(
            "SMS dispatched to phone {PhoneNumber} ({Length} characters)",
            phoneNumber,
            message?.Length ?? 0);

        return Task.CompletedTask;
    }
}
