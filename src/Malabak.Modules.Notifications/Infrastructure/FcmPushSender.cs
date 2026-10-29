using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Malabak.Modules.Notifications.Application;
using Malabak.SharedKernel;

namespace Malabak.Modules.Notifications.Infrastructure;

/// <summary>Firebase service-account settings (from the secret store).</summary>
public sealed record FcmOptions(string ProjectId, string ClientEmail, string PrivateKeyPem);

/// <summary>
/// FCM HTTP v1 sender. Authenticates with a service-account JWT (RS256) exchanged for a short-lived OAuth token,
/// cached until shortly before expiry. Maps FCM errors to retry / drop-token decisions.
/// </summary>
public sealed class FcmPushSender : IPushSender, IDisposable
{
    private const string _scope = "https://www.googleapis.com/auth/firebase.messaging";
    private const string _tokenEndpoint = "https://oauth2.googleapis.com/token";
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly FcmOptions _options;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTime _accessTokenExpiresAtUtc;

    public FcmPushSender(FcmOptions options, IClock clock)
    {
        _options = options;
        _clock = clock;
    }

    public async Task<PushSendResult> SendAsync(PushPayload payload, CancellationToken cancellationToken = default)
    {
        string accessToken;
        try
        {
            accessToken = await GetAccessTokenAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CryptographicException)
        {
            return new PushSendResult(PushFailureKind.Transient, $"auth:{ex.GetType().Name}");
        }

        var body = new
        {
            message = new
            {
                token = payload.Token,
                notification = new { title = payload.Title, body = payload.Body },
                data = payload.Data,
                android = new { priority = "HIGH" },
                apns = new { headers = new Dictionary<string, string> { ["apns-priority"] = "10" } }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(_options.ProjectId)}/messages:send")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return PushSendResult.Ok;
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            return Classify(response.StatusCode, error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new PushSendResult(PushFailureKind.Transient, ex.GetType().Name);
        }
    }

    /// <summary>UNREGISTERED / invalid token → drop the token; 429 / 5xx → retry; anything else → give up.</summary>
    internal static PushSendResult Classify(HttpStatusCode status, string errorBody)
    {
        if (status == HttpStatusCode.NotFound || errorBody.Contains("UNREGISTERED", StringComparison.Ordinal)
            || (status == HttpStatusCode.BadRequest && errorBody.Contains("registration token", StringComparison.OrdinalIgnoreCase)))
        {
            return new PushSendResult(PushFailureKind.InvalidToken, "unregistered");
        }

        if (status == HttpStatusCode.TooManyRequests || (int)status >= 500)
        {
            return new PushSendResult(PushFailureKind.Transient, $"http_{(int)status}");
        }

        return new PushSendResult(PushFailureKind.Permanent, $"http_{(int)status}");
    }

    public void Dispose() => _tokenLock.Dispose();

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _clock.UtcNow < _accessTokenExpiresAtUtc)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _clock.UtcNow < _accessTokenExpiresAtUtc)
            {
                return _accessToken;
            }

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = CreateAssertion()
            });

            using var response = await _httpClient.PostAsync(_tokenEndpoint, form, cancellationToken);
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken: cancellationToken)
                ?? throw new HttpRequestException("Empty OAuth response.");

            _accessToken = token.AccessToken;
            _accessTokenExpiresAtUtc = _clock.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 120));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private string CreateAssertion()
    {
        var now = new DateTimeOffset(_clock.UtcNow).ToUnixTimeSeconds();
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = _options.ClientEmail,
            scope = _scope,
            aud = _tokenEndpoint,
            iat = now,
            exp = now + 3600
        }));

        using var rsa = RSA.Create();
        rsa.ImportFromPem(_options.PrivateKeyPem);
        var signature = rsa.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{claims}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}

/// <summary>Used when FCM isn't configured (e.g. local development): pushes are dropped, in-app notifications still work.</summary>
public sealed class DisabledPushSender : IPushSender
{
    public Task<PushSendResult> SendAsync(PushPayload payload, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PushSendResult(PushFailureKind.Permanent, "push_not_configured"));
}
