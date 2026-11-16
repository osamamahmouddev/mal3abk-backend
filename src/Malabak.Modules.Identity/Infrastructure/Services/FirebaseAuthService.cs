using System.Security.Claims;
using System.Text.Json;
using Malabak.Modules.Identity.Application.Common;
using Malabak.SharedKernel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Malabak.Modules.Identity.Infrastructure.Services;

public sealed record FirebaseOptions
{
    public const string SectionName = "Firebase";
    public string ProjectId { get; set; } = "mal3abk-bd95c";
    public string JwksUrl { get; set; } = "https://www.googleapis.com/service_accounts/v1/jwk/securetoken@system.gserviceaccount.com";
}

public sealed class FirebaseAuthService : IFirebaseAuthService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly FirebaseOptions _options;
    private readonly IHostEnvironment? _environment;
    private readonly ILogger<FirebaseAuthService> _logger;
    private readonly JsonWebTokenHandler _tokenHandler = new();

    private JsonWebKeySet? _cachedJwks;
    private DateTime _jwksExpiresAtUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _jwksLock = new(1, 1);

    public FirebaseAuthService(
        IOptions<FirebaseOptions> options,
        ILogger<FirebaseAuthService> logger,
        IHostEnvironment? environment = null)
    {
        _options = options.Value;
        _logger = logger;
        _environment = environment;
    }

    public async Task<Result<FirebaseTokenClaims>> VerifyIdTokenAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return Result.Failure<FirebaseTokenClaims>(new Error("Identity.InvalidFirebaseToken", "Firebase token cannot be empty."));
        }

        // Development / testing bypass token support: format "dev:+201012345678" or "test:+201012345678"
        if (_environment != null &&
            IdentityEnvironment.AllowsDevelopmentShortcuts(_environment) &&
            (idToken.StartsWith("dev:", StringComparison.OrdinalIgnoreCase) || idToken.StartsWith("test:", StringComparison.OrdinalIgnoreCase)))
        {
            var parts = idToken.Split(':', 2);
            var phone = parts.Length > 1 ? parts[1] : "+201000000000";
            _logger.LogWarning("Bypassing Firebase token verification in development mode for {Phone}", phone);
            return Result.Success(new FirebaseTokenClaims("dev-user-" + Guid.NewGuid().ToString("N")[..8], phone, null, "Dev User"));
        }

        var jwks = await GetJwksAsync(cancellationToken);
        if (jwks == null || jwks.Keys.Count == 0)
        {
            _logger.LogError("Failed to retrieve Google public keys for Firebase token verification.");
            return Result.Failure<FirebaseTokenClaims>(new Error("Identity.FirebaseVerificationUnavailable", "Unable to verify Firebase token at this time."));
        }

        var projectId = _options.ProjectId;
        var validationParameters = new TokenValidationParameters
        {
            ValidIssuer = $"https://securetoken.google.com/{projectId}",
            ValidAudience = projectId,
            IssuerSigningKeys = jwks.Keys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        try
        {
            var result = await _tokenHandler.ValidateTokenAsync(idToken, validationParameters);
            if (!result.IsValid)
            {
                _logger.LogWarning("Firebase token validation failed: {Error}", result.Exception?.Message);
                return Result.Failure<FirebaseTokenClaims>(new Error("Identity.InvalidFirebaseToken", "Invalid or expired Firebase ID token."));
            }

            var claims = result.ClaimsIdentity;
            var uid = claims.FindFirst("sub")?.Value ?? claims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var phoneNumber = claims.FindFirst("phone_number")?.Value;
            var email = claims.FindFirst("email")?.Value;
            var name = claims.FindFirst("name")?.Value;

            if (string.IsNullOrWhiteSpace(uid))
            {
                return Result.Failure<FirebaseTokenClaims>(new Error("Identity.InvalidFirebaseToken", "Firebase token missing subject identifier."));
            }

            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                return Result.Failure<FirebaseTokenClaims>(new Error("Identity.FirebasePhoneMissing", "The Firebase account does not have a verified phone number."));
            }

            return Result.Success(new FirebaseTokenClaims(uid, phoneNumber, email, name));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error validating Firebase token");
            return Result.Failure<FirebaseTokenClaims>(new Error("Identity.InvalidFirebaseToken", "Invalid or expired Firebase ID token."));
        }
    }

    private async Task<JsonWebKeySet?> GetJwksAsync(CancellationToken cancellationToken)
    {
        if (_cachedJwks != null && DateTime.UtcNow < _jwksExpiresAtUtc)
        {
            return _cachedJwks;
        }

        await _jwksLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedJwks != null && DateTime.UtcNow < _jwksExpiresAtUtc)
            {
                return _cachedJwks;
            }

            var response = await _httpClient.GetStringAsync(_options.JwksUrl, cancellationToken);
            var jwks = new JsonWebKeySet(response);
            _cachedJwks = jwks;
            _jwksExpiresAtUtc = DateTime.UtcNow.AddHours(1);
            return _cachedJwks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Google JWKS for Firebase Auth");
            return _cachedJwks; // Return stale cache if available
        }
        finally
        {
            _jwksLock.Release();
        }
    }
}
