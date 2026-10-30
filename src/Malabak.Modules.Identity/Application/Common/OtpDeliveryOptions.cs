namespace Malabak.Modules.Identity.Application.Common;

public enum OtpDeliveryProvider
{
    /// <summary>No channel configured: OTP requests fail closed with 503 and security alerts are dropped.</summary>
    None,

    /// <summary>
    /// Posts codes and security alerts to a Slack channel through an Incoming Webhook. A stopgap until an SMS
    /// provider is integrated: everyone in the channel can read every code and recovery link, so it is refused
    /// in Production.
    /// </summary>
    Slack,

    /// <summary>
    /// Sends through the cost-controlled SMS gateway (<c>Sms</c> section): country allow-list, spend caps and
    /// failover across the configured providers. In Development with no providers configured it uses Mailpit.
    /// </summary>
    Sms,
}

/// <summary>Selects the channel for everything sent to a user's phone: OTP codes and security alerts.</summary>
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
