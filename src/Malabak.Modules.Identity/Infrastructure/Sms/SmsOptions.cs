using Malabak.Modules.Identity.Application.Common;

namespace Malabak.Modules.Identity.Infrastructure.Sms;

/// <summary>Configuration section "Sms". Credentials come from the secret store, never appsettings.</summary>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>Provider names in failover order, e.g. ["twilio-primary", "twilio-backup"] or ["mailpit"] locally.</summary>
    public List<string> Providers { get; set; } = [];

    /// <summary>E.164 prefixes we deliver to (SMS-pumping protection, SECURITY.md §4.3). Egypt only at launch.</summary>
    public List<string> AllowedCountryPrefixes { get; set; } = ["+20"];

    public decimal DailySpendCapEgp { get; set; } = 2_000m;
    public decimal MonthlySpendCapEgp { get; set; } = 30_000m;

    /// <summary>Fraction of a cap at which an alert is logged (once per send above it).</summary>
    public decimal AlertThreshold { get; set; } = 0.8m;

    /// <summary>Estimated cost per message when the provider doesn't report one.</summary>
    public decimal DefaultCostPerMessageEgp { get; set; } = 0.5m;

    public SmsChannel OtpChannel { get; set; } = SmsChannel.Sms;

    public Dictionary<string, TwilioAccountOptions> Twilio { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TwilioAccountOptions
{
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string? WhatsAppFrom { get; set; }

    /// <summary>Public URL Twilio posts delivery receipts to (must match exactly for signature checks).</summary>
    public string? StatusCallbackUrl { get; set; }
}
