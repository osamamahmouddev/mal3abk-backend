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

    /// <summary>
    /// Sends OTP codes and security alerts via WhatsApp using CodeFiye WhatsApp API.
    /// </summary>
    WhatsApp,

    /// <summary>
    /// Sends OTP codes and alerts directly via official Meta WhatsApp Business Cloud API (graph.facebook.com).
    /// </summary>
    MetaWhatsApp,
}

/// <summary>Selects the channel for everything sent to a user's phone: OTP codes and security alerts.</summary>
public sealed class OtpDeliveryOptions
{
    public const string SectionName = "Identity:OtpDelivery";

    public OtpDeliveryProvider Provider { get; set; } = OtpDeliveryProvider.None;

    public SlackOtpDeliveryOptions Slack { get; set; } = new();

    public WhatsAppOtpDeliveryOptions WhatsApp { get; set; } = new();

    public MetaWhatsAppOtpDeliveryOptions MetaWhatsApp { get; set; } = new();
}

public sealed class WhatsAppOtpDeliveryOptions
{
    /// <summary>CodeFiye API Key (session key was_... or workspace key wap_...).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Session ID (required when using workspace key wap_...).</summary>
    public string? SessionId { get; set; }

    /// <summary>CodeFiye API Base URL.</summary>
    public string BaseUrl { get; set; } = "https://wa.codefiye.com";
}

public sealed class MetaWhatsAppOtpDeliveryOptions
{
    /// <summary>Meta WhatsApp Phone Number ID (from developers.facebook.com).</summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>Meta System User or User Access Token (Bearer token).</summary>
    public string? AccessToken { get; set; }

    /// <summary>Approved WhatsApp Template Name (default 'hello_world' for testing).</summary>
    public string TemplateName { get; set; } = "hello_world";

    /// <summary>Template Language Code (e.g. en_US, ar).</summary>
    public string LanguageCode { get; set; } = "en_US";

    /// <summary>Meta Graph API Base URL.</summary>
    public string BaseUrl { get; set; } = "https://graph.facebook.com";

    /// <summary>Meta Graph API Version (e.g. v21.0).</summary>
    public string ApiVersion { get; set; } = "v21.0";
}

public sealed class SlackOtpDeliveryOptions
{
    /// <summary>Incoming Webhook URL. It is a credential: keep it in User Secrets or the secret store.</summary>
    public string? WebhookUrl { get; set; }
}
