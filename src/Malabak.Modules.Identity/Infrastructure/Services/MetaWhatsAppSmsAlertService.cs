using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Hosting;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Delivers security alerts via Meta WhatsApp Business Cloud API.
/// </summary>
public sealed class MetaWhatsAppSmsAlertService : ISmsAlertService
{
    private readonly MetaWhatsAppClient _client;
    private readonly IHostEnvironment _environment;

    public MetaWhatsAppSmsAlertService(MetaWhatsAppClient client, IHostEnvironment environment)
    {
        _client = client;
        _environment = environment;
    }

    public Task SendSecurityAlertAsync(string phoneNumber, SecurityAlert alert, CancellationToken cancellationToken = default)
    {
        var message = SlackSmsAlertService.BuildMessage(phoneNumber, alert, _environment.EnvironmentName);
        return _client.SendMessageAsync(phoneNumber, message, $"{alert.Kind} security alert", cancellationToken);
    }
}
