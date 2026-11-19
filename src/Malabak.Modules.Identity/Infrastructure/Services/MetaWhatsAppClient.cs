using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Communicates directly with Meta's official WhatsApp Business Cloud API (graph.facebook.com).
/// Sends OTP codes using approved templates or fallback to Slack if delivery fails.
/// Complies with SECURITY.md §13: credentials and OTP codes are never logged.
/// </summary>
public sealed class MetaWhatsAppClient
{
    private readonly HttpClient _httpClient;
    private readonly OtpDeliveryOptions _options;
    private readonly SlackWebhookClient? _slackFallback;
    private readonly ILogger<MetaWhatsAppClient> _logger;

    public MetaWhatsAppClient(
        HttpClient httpClient,
        IOptions<OtpDeliveryOptions> options,
        ILogger<MetaWhatsAppClient> logger,
        SlackWebhookClient? slackFallback = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _slackFallback = slackFallback;
    }

    public async Task<bool> SendOtpAsync(
        string phoneNumber,
        string code,
        OtpPurpose purpose,
        TimeSpan validity,
        CancellationToken cancellationToken = default)
    {
        var meta = _options.MetaWhatsApp;
        if (string.IsNullOrWhiteSpace(meta.AccessToken) || string.IsNullOrWhiteSpace(meta.PhoneNumberId))
        {
            _logger.LogWarning("Meta WhatsApp credentials (AccessToken / PhoneNumberId) are not configured.");
            return await FallbackToSlackAsync(phoneNumber, code, purpose, validity, "Meta credentials missing", cancellationToken);
        }

        try
        {
            // Meta Graph API expects phone number without '+' (e.g. 201150721902)
            var cleanPhone = phoneNumber.Trim().TrimStart('+');
            var apiVersion = string.IsNullOrWhiteSpace(meta.ApiVersion) ? "v21.0" : meta.ApiVersion;
            var endpoint = $"/{apiVersion}/{meta.PhoneNumberId}/messages";

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", meta.AccessToken);

            object payload;
            if (string.Equals(meta.TemplateName, "hello_world", StringComparison.OrdinalIgnoreCase))
            {
                // Test starter template bundled by Meta on every WhatsApp app
                payload = new
                {
                    messaging_product = "whatsapp",
                    to = cleanPhone,
                    type = "template",
                    template = new
                    {
                        name = "hello_world",
                        language = new { code = "en_US" }
                    }
                };
            }
            else
            {
                // Custom OTP template with code parameter
                payload = new
                {
                    messaging_product = "whatsapp",
                    to = cleanPhone,
                    type = "template",
                    template = new
                    {
                        name = meta.TemplateName,
                        language = new { code = meta.LanguageCode },
                        components = new object[]
                        {
                            new
                            {
                                type = "body",
                                parameters = new object[]
                                {
                                    new { type = "text", text = code }
                                }
                            },
                            new
                            {
                                type = "button",
                                sub_type = "url",
                                index = "0",
                                parameters = new object[]
                                {
                                    new { type = "text", text = code }
                                }
                            }
                        }
                    }
                };
            }

            request.Content = JsonContent.Create(payload);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadFromJsonAsync<MetaApiResponse>(cancellationToken: cancellationToken);

            if (response.IsSuccessStatusCode && content?.Messages is { Length: > 0 })
            {
                _logger.LogInformation("Meta WhatsApp OTP template successfully sent (purpose: {Purpose}).", purpose);
                return true;
            }

            var errorCode = content?.Error?.Code.ToString() ?? ((int)response.StatusCode).ToString();
            var errorMessage = content?.Error?.Message ?? "Unknown error";
            _logger.LogWarning("Meta WhatsApp API failed: {ErrorCode} - {ErrorMessage}", errorCode, errorMessage);

            return await FallbackToSlackAsync(phoneNumber, code, purpose, validity, $"Meta: {errorCode}", cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "HTTP exception while calling Meta WhatsApp Cloud API.");
            return await FallbackToSlackAsync(phoneNumber, code, purpose, validity, "Meta connection error", cancellationToken);
        }
    }

    public async Task<bool> SendMessageAsync(
        string phoneNumber,
        string text,
        string messageKind,
        CancellationToken cancellationToken = default)
    {
        var meta = _options.MetaWhatsApp;
        if (string.IsNullOrWhiteSpace(meta.AccessToken) || string.IsNullOrWhiteSpace(meta.PhoneNumberId))
        {
            _logger.LogWarning("Meta WhatsApp credentials not configured for {MessageKind}.", messageKind);
            if (_slackFallback != null)
            {
                return await _slackFallback.PostAsync(text, messageKind, cancellationToken);
            }
            return false;
        }

        try
        {
            var cleanPhone = phoneNumber.Trim().TrimStart('+');
            var apiVersion = string.IsNullOrWhiteSpace(meta.ApiVersion) ? "v21.0" : meta.ApiVersion;
            var endpoint = $"/{apiVersion}/{meta.PhoneNumberId}/messages";

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", meta.AccessToken);

            var payload = new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = cleanPhone,
                type = "text",
                text = new { preview_url = false, body = text }
            };

            request.Content = JsonContent.Create(payload);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Meta WhatsApp {MessageKind} sent successfully.", messageKind);
                return true;
            }

            _logger.LogWarning("Meta WhatsApp send-message failed with status {StatusCode}.", (int)response.StatusCode);
            if (_slackFallback != null)
            {
                return await _slackFallback.PostAsync(text, messageKind, cancellationToken);
            }
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "HTTP exception calling Meta WhatsApp send-message for {MessageKind}.", messageKind);
            if (_slackFallback != null)
            {
                return await _slackFallback.PostAsync(text, messageKind, cancellationToken);
            }
            return false;
        }
    }

    private async Task<bool> FallbackToSlackAsync(
        string phoneNumber,
        string code,
        OtpPurpose purpose,
        TimeSpan validity,
        string reason,
        CancellationToken cancellationToken)
    {
        if (_slackFallback == null || string.IsNullOrWhiteSpace(_options.Slack.WebhookUrl))
        {
            return false;
        }

        _logger.LogInformation("Falling back to Slack for OTP delivery (reason: {Reason}).", reason);
        var message = SlackOtpSender.BuildMessage(phoneNumber, code, purpose, validity, $"Meta Fallback - {reason}");
        return await _slackFallback.PostAsync(message, $"{purpose} OTP (Slack Fallback)", cancellationToken);
    }

    private sealed class MetaApiResponse
    {
        [JsonPropertyName("messages")]
        public MetaMessageIdentifier[]? Messages { get; set; }

        [JsonPropertyName("error")]
        public MetaApiError? Error { get; set; }
    }

    private sealed class MetaMessageIdentifier
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    private sealed class MetaApiError
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }
}
