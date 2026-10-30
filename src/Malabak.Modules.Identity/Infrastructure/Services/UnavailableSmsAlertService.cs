using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Used when no delivery channel is configured. The change the alert reports is already committed, so the alert
/// is dropped with a warning that names only its kind: never the phone number, the content or a recovery token.
/// </summary>
public sealed class UnavailableSmsAlertService : ISmsAlertService
{
    private readonly ILogger<UnavailableSmsAlertService> _logger;

    public UnavailableSmsAlertService(ILogger<UnavailableSmsAlertService> logger)
    {
        _logger = logger;
    }

    public Task SendSecurityAlertAsync(string phoneNumber, SecurityAlert alert, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "No delivery channel is configured ({Section}:Provider is None); {AlertKind} security alert was not sent.",
            OtpDeliveryOptions.SectionName,
            alert.Kind);

        return Task.CompletedTask;
    }
}
