using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Hosting;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Sends security alerts to the user's WhatsApp using CodeFiye WhatsApp API.
/// </summary>
public sealed class WhatsAppSmsAlertService : ISmsAlertService
{
    private readonly CodeFiyeClient _client;
    private readonly IHostEnvironment _environment;

    public WhatsAppSmsAlertService(CodeFiyeClient client, IHostEnvironment environment)
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
