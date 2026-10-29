namespace Malabak.Modules.Identity.Application.Common;

public enum OtpDeliveryProvider
{
    /// <summary>No channel configured: OTP requests fail closed with 503.</summary>
    None,

    /// <summary>
    /// Posts codes to a Slack channel through an Incoming Webhook. A stopgap until an SMS provider is
    /// integrated: everyone in the channel can read every code, so it is refused in Production.
    /// </summary>
    Slack,
}

public sealed class OtpDeliveryOptions
{
    public const string SectionName = "Identity:OtpDelivery";

    public OtpDeliveryProvider Provider { get; set; } = OtpDeliveryProvider.None;

    public SlackOtpDeliveryOptions Slack { get; set; } = new();
}

public sealed class SlackOtpDeliveryOptions
{
    /// <summary>Incoming Webhook URL. It is a credential: keep it in User Secrets or the secret store.</summary>
    public string? WebhookUrl { get; set; }
}
