using System.Globalization;
using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Hosting;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Posts one-time codes to a Slack channel through an Incoming Webhook, for testing before an SMS
/// provider is integrated. The phone number is masked in the message; the code and the webhook URL
/// are never logged. Refused in Production by the options validation in <see cref="IdentityModule"/>.
/// </summary>
public sealed class SlackOtpSender : IOtpSender
{
    private readonly SlackWebhookClient _slack;
    private readonly IHostEnvironment _environment;

    public SlackOtpSender(SlackWebhookClient slack, IHostEnvironment environment)
    {
        _slack = slack;
        _environment = environment;
    }

    public Task<bool> SendAsync(
        string phoneNumber,
        string code,
        OtpPurpose purpose,
        TimeSpan validity,
        CancellationToken cancellationToken = default) =>
        _slack.PostAsync(BuildMessage(phoneNumber, code, purpose, validity, _environment.EnvironmentName), $"{purpose} OTP", cancellationToken);

    public static string BuildMessage(string phoneNumber, string code, OtpPurpose purpose, TimeSpan validity, string environmentName)
    {
        var purposeText = purpose switch
        {
            OtpPurpose.Login => "تسجيل الدخول",
            OtpPurpose.ChangePhoneOld => "تغيير الرقم (تأكيد الرقم الحالي)",
            OtpPurpose.ChangePhoneNew => "تغيير الرقم (تأكيد الرقم الجديد)",
            _ => purpose.ToString(),
        };

        var minutes = ((int)Math.Ceiling(validity.TotalMinutes)).ToString(CultureInfo.InvariantCulture);

        return $"""
            *ملعبك — كود تحقق* ({environmentName})
            الرقم: `{MaskPhoneNumber(phoneNumber)}`
            الغرض: {purposeText}
            الكود: *{code}*
            صالح لمدة {minutes} دقيقة
            """;
    }

    /// <summary>Keeps the country and operator prefix and the last three digits: +201012345678 → +2010•••••678.</summary>
    public static string MaskPhoneNumber(string phoneNumber)
    {
        const int visiblePrefix = 5;
        const int visibleSuffix = 3;

        if (string.IsNullOrEmpty(phoneNumber) || phoneNumber.Length <= visiblePrefix + visibleSuffix)
        {
            return new string('•', phoneNumber?.Length ?? 0);
        }

        var hidden = phoneNumber.Length - visiblePrefix - visibleSuffix;
        return string.Concat(phoneNumber.AsSpan(0, visiblePrefix), new string('•', hidden), phoneNumber.AsSpan(phoneNumber.Length - visibleSuffix));
    }
}
