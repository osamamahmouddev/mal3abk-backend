using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Options;

namespace Malabak.Modules.Identity.Infrastructure.Sms;

/// <summary>
/// Twilio Messages REST API (SMS and WhatsApp), called directly over HTTPS so no vendor SDK is needed.
/// One instance per configured account name (e.g. "twilio-primary", "twilio-backup") enables failover.
/// </summary>
public sealed class TwilioSmsProvider : ISmsProvider
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly TwilioAccountOptions _account;

    public TwilioSmsProvider(string name, TwilioAccountOptions account)
    {
        Name = name;
        _account = account;
    }

    public string Name { get; }

    public bool Supports(SmsChannel channel) => channel == SmsChannel.Sms || !string.IsNullOrWhiteSpace(_account.WhatsAppFrom);

    public async Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_account.AccountSid) || string.IsNullOrWhiteSpace(_account.AuthToken))
        {
            return SmsSendResult.Failed("not_configured");
        }

        var isWhatsApp = message.Channel == SmsChannel.WhatsApp;
        var form = new Dictionary<string, string>
        {
            ["To"] = isWhatsApp ? $"whatsapp:{message.ToE164}" : message.ToE164,
            ["From"] = isWhatsApp ? $"whatsapp:{_account.WhatsAppFrom}" : _account.FromNumber,
            ["Body"] = message.Body
        };

        if (!string.IsNullOrWhiteSpace(_account.StatusCallbackUrl))
        {
            form["StatusCallback"] = _account.StatusCallbackUrl;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(_account.AccountSid)}/Messages.json")
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_account.AccountSid}:{_account.AuthToken}")));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return SmsSendResult.Failed($"http_{(int)response.StatusCode}");
        }

        var payload = await response.Content.ReadFromJsonAsync<TwilioMessageResponse>(cancellationToken: cancellationToken);
        return new SmsSendResult(true, payload?.Sid, 0m, null);
    }

    /// <summary>
    /// Twilio webhook signature: base64(HMAC-SHA1(authToken, url + sorted(key+value)...)) in X-Twilio-Signature.
    /// </summary>
    public static bool IsValidSignature(string authToken, string url, IEnumerable<KeyValuePair<string, string>> form, string? signature)
    {
        if (string.IsNullOrWhiteSpace(authToken) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var data = new StringBuilder(url);
        foreach (var (key, value) in form.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            data.Append(key).Append(value);
        }

        // HMAC-SHA1 is what Twilio's protocol mandates; it's used for verification only.
#pragma warning disable CA5350
        var expected = HMACSHA1.HashData(Encoding.UTF8.GetBytes(authToken), Encoding.UTF8.GetBytes(data.ToString()));
#pragma warning restore CA5350

        byte[] provided;
        try
        {
            provided = Convert.FromBase64String(signature);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    private sealed class TwilioMessageResponse
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }
    }
}

/// <summary>
/// Development-only provider: delivers each message as an email to the local Mailpit inbox (sms+NUMBER@malabak.local),
/// so OTP flows can be tested end-to-end without a real SMS account and without logging codes.
/// </summary>
public sealed class MailpitSmsProvider : ISmsProvider
{
    public const string ProviderName = "mailpit";

    private readonly IEmailSender _email;

    public MailpitSmsProvider(IEmailSender email)
    {
        _email = email;
    }

    public string Name => ProviderName;

    public bool Supports(SmsChannel channel) => true;

    public async Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        var digits = new string(message.ToE164.Where(char.IsAsciiDigit).ToArray());
        await _email.SendAsync($"sms+{digits}@malabak.local", $"[{message.Channel}] {message.Template}", message.Body, cancellationToken);
        return new SmsSendResult(true, Guid.NewGuid().ToString("N"), 0.01m, null);
    }
}

public static class SmsProviderFactory
{
    /// <summary>Creates the configured providers: Twilio accounts by name, plus Mailpit in Development.</summary>
    public static IEnumerable<ISmsProvider> Create(IOptions<SmsOptions> options, IEmailSender email, bool isDevelopment)
    {
        foreach (var (name, account) in options.Value.Twilio)
        {
            yield return new TwilioSmsProvider(name, account);
        }

        if (isDevelopment)
        {
            yield return new MailpitSmsProvider(email);
        }
    }
}
