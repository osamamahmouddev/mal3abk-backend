namespace Malabak.Modules.Identity.Application.Common;

public enum EmailDeliveryProvider
{
    /// <summary>SMTP relay from Email:Smtp:* (Mailpit on localhost:1025 locally).</summary>
    Smtp,

    /// <summary>
    /// Posts every account email (verification, password reset, staff invitation, new-device alert) to a Slack
    /// channel, for testing before a real mail provider is set up. Everyone in the channel can open every link,
    /// so it is refused in Production.
    /// </summary>
    Slack,
}

/// <summary>Selects where account emails go.</summary>
public sealed class EmailDeliveryOptions
{
    public const string SectionName = "Identity:EmailDelivery";

    public EmailDeliveryProvider Provider { get; set; } = EmailDeliveryProvider.Smtp;

    public SlackEmailDeliveryOptions Slack { get; set; } = new();
}

public sealed class SlackEmailDeliveryOptions
{
    /// <summary>
    /// Incoming Webhook URL for the email channel. It is a credential: keep it in User Secrets or the secret store.
    /// When empty, the OTP channel's webhook (Identity:OtpDelivery:Slack:WebhookUrl) is used.
    /// </summary>
    public string? WebhookUrl { get; set; }
}
