using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Hosting;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Posts security alerts to the same Slack channel as OTP codes, for testing before an SMS provider is
/// integrated. Phone numbers are masked in the message; the recovery link is shown because testers need it,
/// which is one more reason the Slack provider is refused in Production. Nothing but the alert kind is logged.
/// </summary>
public sealed class SlackSmsAlertService : ISmsAlertService
{
    private readonly SlackWebhookClient _slack;
    private readonly IHostEnvironment _environment;

    public SlackSmsAlertService(SlackWebhookClient slack, IHostEnvironment environment)
    {
        _slack = slack;
        _environment = environment;
    }

    public Task SendSecurityAlertAsync(string phoneNumber, SecurityAlert alert, CancellationToken cancellationToken = default) =>
        _slack.PostAsync(BuildMessage(phoneNumber, alert, _environment.EnvironmentName), $"{alert.Kind} security alert", cancellationToken);

    public static string BuildMessage(string phoneNumber, SecurityAlert alert, string environmentName)
    {
        var header = $"""
            *ملعبك — تنبيه أمان* ({environmentName})
            الرقم: `{SlackOtpSender.MaskPhoneNumber(phoneNumber)}`
            """;

        return alert.Kind switch
        {
            SecurityAlertKind.PhoneChanged => $"""
                {header}
                النوع: تم تغيير رقم الحساب إلى `{SlackOtpSender.MaskPhoneNumber(alert.NewPhoneNumber ?? string.Empty)}`
                رابط الاسترجاع (صالح 24 ساعة): {alert.RecoveryLink}
                """,
            SecurityAlertKind.PhoneRestored => $"""
                {header}
                النوع: تم استرجاع رقم الحساب وإنهاء كل الجلسات الأخرى
                """,
            _ => $"""
                {header}
                النوع: {alert.Kind}
                """,
        };
    }
}
