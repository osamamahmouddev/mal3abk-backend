namespace Malabak.Modules.Identity.Application.Common;

public enum SmsChannel
{
    Sms = 0,
    WhatsApp = 1
}

public sealed record SmsMessage(string ToE164, string Body, SmsChannel Channel, string Template);

public sealed record SmsSendResult(bool Success, string? ProviderMessageId, decimal CostEgp, string? Error)
{
    public static SmsSendResult Failed(string error) => new(false, null, 0m, error);
}

/// <summary>One SMS/WhatsApp vendor. Adapters must never log the message body (it can contain OTP codes).</summary>
public interface ISmsProvider
{
    string Name { get; }

    bool Supports(SmsChannel channel);

    Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken = default);
}

public enum SmsSendOutcome
{
    Sent = 0,
    CountryNotAllowed = 1,
    SpendCapReached = 2,
    AllProvidersFailed = 3
}

/// <summary>Cost-controlled entry point used by OTP and security alerts.</summary>
public interface ISmsGateway
{
    /// <summary>Sends an OTP on the configured channel (SMS or WhatsApp).</summary>
    Task<SmsSendOutcome> SendOtpAsync(string toE164, string code, CancellationToken cancellationToken = default);

    Task<SmsSendOutcome> SendAsync(string toE164, string body, string template, SmsChannel channel = SmsChannel.Sms,
        CancellationToken cancellationToken = default);
}

/// <summary>Message templates, so wording and cost are reviewed in one place.</summary>
public static class SmsTemplates
{
    public const string Otp = "otp";
    public const string SecurityAlert = "security_alert";

    public static string OtpBody(string code) => $"رمز التحقق في ملعبك: {code}. صالح 5 دقائق. لا تشاركه مع أحد.\nMalabak code: {code}";
}
