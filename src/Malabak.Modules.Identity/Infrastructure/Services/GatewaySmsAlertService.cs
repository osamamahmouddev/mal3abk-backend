using Malabak.Modules.Identity.Application.Common;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>Security alerts and phone-change codes go through the cost-controlled SMS gateway (never logged).</summary>
public sealed class GatewaySmsAlertService : ISmsAlertService
{
    private readonly ISmsGateway _gateway;

    public GatewaySmsAlertService(ISmsGateway gateway)
    {
        _gateway = gateway;
    }

    public Task SendSecurityAlertAsync(string phoneNumber, string message, CancellationToken cancellationToken = default) =>
        _gateway.SendAsync(phoneNumber, message, SmsTemplates.SecurityAlert, SmsChannel.Sms, cancellationToken);
}
