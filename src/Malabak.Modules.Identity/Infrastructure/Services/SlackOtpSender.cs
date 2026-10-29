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
            OtpPurpose.StepUp => "تأكيد الهوية لعملية حساسة",
            _ => purpose.ToString(),
        };

        var minutes = ((int)Math.Ceiling(validity.TotalMinutes)).ToString(CultureInfo.InvariantCulture);

        return $"""
            *ملعبك — كود تحقق* ({environmentName})
            الرقم: `{PhoneNumberMasker.Mask(phoneNumber)}`
            الغرض: {purposeText}
            الكود: *{code}*
            صالح لمدة {minutes} دقيقة
            """;
    }
}
