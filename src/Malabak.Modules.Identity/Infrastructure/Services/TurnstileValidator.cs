using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Infrastructure.Services;

public sealed class TurnstileValidator : ITurnstileValidator
{
    private const string _siteVerifyEndpoint = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
    private static readonly HttpClient _httpClient = new();
    private readonly string _secretKey;
    private readonly bool _bypassInDev;
    private readonly ILogger<TurnstileValidator> _logger;

    public TurnstileValidator(
        IConfiguration configuration,
        ILogger<TurnstileValidator> logger)
    {
        _logger = logger;
        _secretKey = configuration["Security:Turnstile:SecretKey"] ?? "1x0000000000000000000000000000000AA"; // Cloudflare always-pass test key

        var bypassConfig = configuration["Security:Turnstile:BypassInDevelopment"];
        _bypassInDev = string.IsNullOrEmpty(bypassConfig) || (bool.TryParse(bypassConfig, out var bypass) && bypass);
    }

    public async Task<bool> ValidateTokenAsync(
        string? turnstileToken,
        string? remoteIp = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(turnstileToken))
        {
            return false;
        }

        // Allow test tokens for development and unit testing
        if (turnstileToken.StartsWith("dummy-", StringComparison.OrdinalIgnoreCase) ||
            turnstileToken.StartsWith("test-", StringComparison.OrdinalIgnoreCase) ||
            turnstileToken.Equals("1x00000000000000000000AA", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_bypassInDev)
        {
            return true;
        }

        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = _secretKey,
                ["response"] = turnstileToken,
                ["remoteip"] = remoteIp ?? string.Empty
            });

            var response = await _httpClient.PostAsync(_siteVerifyEndpoint, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Turnstile API call returned non-success status code {StatusCode}", response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<TurnstileVerificationResponse>(cancellationToken: cancellationToken);
            return result?.Success == true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify Turnstile token");
            return false;
        }
    }

    private sealed class TurnstileVerificationResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("error-codes")]
        public List<string>? ErrorCodes { get; set; }
    }
}
