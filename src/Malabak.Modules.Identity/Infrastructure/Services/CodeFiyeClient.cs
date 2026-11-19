using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Communicates with CodeFiye WhatsApp API (https://wa.codefiye.com) to deliver OTP codes and security alerts.
/// In case the WhatsApp session is disconnected, it logs a warning and gracefully forwards to Slack (if configured)
/// so developers and testers are never locked out.
/// Complies with SECURITY.md §13: credentials and OTP codes are never logged.
/// </summary>
public sealed class CodeFiyeClient
{
    private readonly HttpClient _httpClient;
    private readonly OtpDeliveryOptions _options;
    private readonly SlackWebhookClient? _slackFallback;
    private readonly ILogger<CodeFiyeClient> _logger;

    public CodeFiyeClient(
        HttpClient httpClient,
        IOptions<OtpDeliveryOptions> options,
        ILogger<CodeFiyeClient> logger,
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
        var apiKey = _options.WhatsApp.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("CodeFiye WhatsApp API key is not configured.");
            return await FallbackToSlackAsync(phoneNumber, code, purpose, validity, "WhatsApp API key missing", cancellationToken);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/send-otp");
            ApplyAuthHeaders(request, apiKey);

            request.Content = JsonContent.Create(new
            {
                to = phoneNumber,
                code = code,
                lang = "ar",
                length = code.Length
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadFromJsonAsync<CodeFiyeApiResponse>(cancellationToken: cancellationToken);

            if (response.IsSuccessStatusCode && content is { Success: true })
            {
                _logger.LogInformation("WhatsApp OTP successfully queued for delivery via CodeFiye (purpose: {Purpose}).", purpose);
                return true;
            }

            var errorCode = content?.Code ?? response.StatusCode.ToString();
            var errorMessage = content?.Message ?? "Unknown error";
            _logger.LogWarning("CodeFiye WhatsApp OTP failed: {ErrorCode} - {ErrorMessage}", errorCode, errorMessage);

            return await FallbackToSlackAsync(phoneNumber, code, purpose, validity, $"CodeFiye: {errorCode}", cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "HTTP exception while calling CodeFiye WhatsApp API.");
            return await FallbackToSlackAsync(phoneNumber, code, purpose, validity, "HTTP connection error", cancellationToken);
        }
    }

    public async Task<bool> SendMessageAsync(
        string phoneNumber,
        string text,
        string messageKind,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _options.WhatsApp.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("CodeFiye WhatsApp API key is not configured for {MessageKind}.", messageKind);
            if (_slackFallback != null)
            {
                return await _slackFallback.PostAsync(text, messageKind, cancellationToken);
            }
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/send-message");
            ApplyAuthHeaders(request, apiKey);

            request.Content = JsonContent.Create(new
            {
                to = phoneNumber,
                text = text
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("WhatsApp {MessageKind} successfully sent via CodeFiye.", messageKind);
                return true;
            }

            _logger.LogWarning("CodeFiye WhatsApp send-message failed with status {StatusCode}.", (int)response.StatusCode);
            if (_slackFallback != null)
            {
                return await _slackFallback.PostAsync(text, messageKind, cancellationToken);
            }
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "HTTP exception while calling CodeFiye send-message for {MessageKind}.", messageKind);
            if (_slackFallback != null)
            {
                return await _slackFallback.PostAsync(text, messageKind, cancellationToken);
            }
            return false;
        }
    }

    private void ApplyAuthHeaders(HttpRequestMessage request, string apiKey)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // When using a workspace token (wap_...), CodeFiye requires X-Session-Id
        if (apiKey.StartsWith("wap_", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(_options.WhatsApp.SessionId))
        {
            request.Headers.Add("X-Session-Id", _options.WhatsApp.SessionId);
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
        var message = SlackOtpSender.BuildMessage(phoneNumber, code, purpose, validity, $"Development Fallback - {reason}");
        return await _slackFallback.PostAsync(message, $"{purpose} OTP (Slack Fallback)", cancellationToken);
    }

    private sealed class CodeFiyeApiResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }
    }
}
