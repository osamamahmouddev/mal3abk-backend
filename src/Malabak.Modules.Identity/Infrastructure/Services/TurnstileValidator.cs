using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Malabak.Modules.Identity.Infrastructure.Services;

public sealed class TurnstileValidator : ITurnstileValidator
{
    // Cloudflare's published Turnstile test site key, accepted as a token only with the test tokens below.
    private const string _testSiteKey = "1x00000000000000000000AA";
    private static readonly HttpClient _httpClient = new();
    private readonly TurnstileOptions _options;
    private readonly bool _allowTestTokens;
    private readonly bool _bypass;
    private readonly ILogger<TurnstileValidator> _logger;

    public TurnstileValidator(
        IOptions<TurnstileOptions> options,
        IHostEnvironment environment,
        ILogger<TurnstileValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger;

        // Startup validation already refuses the bypass outside Development/Testing; checking the
        // environment again here keeps a misbound option from switching Turnstile off.
        _allowTestTokens = IdentityEnvironment.AllowsDevelopmentShortcuts(environment);
        _bypass = _allowTestTokens && _options.BypassInDevelopment;
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

        if (_allowTestTokens && IsTestToken(turnstileToken))
        {
            return true;
        }

        if (_bypass)
        {
            return true;
        }

        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = _options.SecretKey,
                ["response"] = turnstileToken,
                ["remoteip"] = remoteIp ?? string.Empty
            });

            var response = await _httpClient.PostAsync(_options.VerifyUrl, content, cancellationToken);
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

    private static bool IsTestToken(string token) =>
        token.StartsWith("dummy-", StringComparison.OrdinalIgnoreCase) ||
        token.StartsWith("test-", StringComparison.OrdinalIgnoreCase) ||
        token.Equals(_testSiteKey, StringComparison.OrdinalIgnoreCase);

    private sealed class TurnstileVerificationResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("error-codes")]
        public List<string>? ErrorCodes { get; set; }
    }
}
