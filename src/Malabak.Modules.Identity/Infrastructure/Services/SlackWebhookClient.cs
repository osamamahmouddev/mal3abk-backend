using System.Net.Http.Json;
using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Posts a message to the Slack Incoming Webhook configured in <see cref="OtpDeliveryOptions"/>. Shared by the
/// OTP and security-alert senders. The message text and the webhook URL (a credential) are never logged; failures
/// name only the caller-supplied <c>messageKind</c>.
/// </summary>
public sealed class SlackWebhookClient
{
    private readonly HttpClient _httpClient;
    private readonly OtpDeliveryOptions _options;
    private readonly ILogger<SlackWebhookClient> _logger;

    public SlackWebhookClient(HttpClient httpClient, IOptions<OtpDeliveryOptions> options, ILogger<SlackWebhookClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> PostAsync(string text, string messageKind, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(_options.Slack.WebhookUrl, new { text }, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            _logger.LogWarning("Slack rejected the {MessageKind} message with status {StatusCode}.", messageKind, (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Connection errors name only host:port; the webhook path (the secret part) is not in them.
            _logger.LogWarning(ex, "Posting the {MessageKind} message to Slack failed.", messageKind);
            return false;
        }
    }
}
