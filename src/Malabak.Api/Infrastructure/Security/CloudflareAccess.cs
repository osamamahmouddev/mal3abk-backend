using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Malabak.Api.Infrastructure.Security;

/// <summary>Cloudflare Access (Zero Trust) settings (TASK-720). Leave <see cref="TeamDomain"/> empty to turn the check off (local dev).</summary>
public sealed class CloudflareAccessOptions
{
    public const string SectionName = "CloudflareAccess";

    /// <summary>e.g. <c>malabak.cloudflareaccess.com</c>.</summary>
    public string? TeamDomain { get; set; }

    /// <summary>Application AUD tags allowed to reach the protected paths (one per Access application).</summary>
    public List<string> Audiences { get; set; } = [];

    /// <summary>Internal tools that must only be reached through Access.</summary>
    public List<string> ProtectedPaths { get; set; } = ["/hangfire", "/swagger"];

    public bool Enabled => !string.IsNullOrWhiteSpace(TeamDomain);
}

/// <summary>
/// Verifies the signed <c>Cf-Access-Jwt-Assertion</c> header (signature with the team's published keys, issuer, audience,
/// lifetime). Unlike the plain email header, it cannot be forged by a client that reaches the origin some other way.
/// </summary>
public sealed class CloudflareAccessValidator
{
    public const string AssertionHeader = "Cf-Access-Jwt-Assertion";
    public static readonly TimeSpan KeyCacheLifetime = TimeSpan.FromHours(1);

    private readonly CloudflareAccessOptions _options;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<CloudflareAccessValidator> _logger;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private IReadOnlyList<SecurityKey> _keys = [];
    private DateTime _keysFetchedAtUtc = DateTime.MinValue;

    public CloudflareAccessValidator(IOptions<CloudflareAccessOptions> options, IHttpClientFactory http, ILogger<CloudflareAccessValidator> logger)
    {
        _options = options.Value;
        _http = http;
        _logger = logger;
    }

    public async Task<bool> IsValidAsync(string? assertion, CancellationToken cancellationToken)
    {
        var handler = new JsonWebTokenHandler();
        if (!_options.Enabled || string.IsNullOrWhiteSpace(assertion) || _options.Audiences.Count == 0 || !handler.CanReadToken(assertion))
        {
            return false;
        }

        IReadOnlyList<SecurityKey> keys;
        try
        {
            keys = await KeysAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException)
        {
            // Fail closed: without the team's keys nothing can be verified.
            _logger.LogError(ex, "Could not load Cloudflare Access signing keys");
            return false;
        }

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = $"https://{_options.TeamDomain}",
            ValidAudiences = _options.Audiences,
            IssuerSigningKeys = keys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        var result = await handler.ValidateTokenAsync(assertion, parameters);
        if (!result.IsValid)
        {
            _logger.LogWarning("Cloudflare Access assertion rejected: {Reason}", result.Exception?.GetType().Name);
        }

        return result.IsValid;
    }

    private async Task<IReadOnlyList<SecurityKey>> KeysAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow - _keysFetchedAtUtc < KeyCacheLifetime && _keys.Count > 0)
        {
            return _keys;
        }

        await _refresh.WaitAsync(cancellationToken);
        try
        {
            if (DateTime.UtcNow - _keysFetchedAtUtc >= KeyCacheLifetime || _keys.Count == 0)
            {
                var json = await _http.CreateClient(nameof(CloudflareAccessValidator))
                    .GetStringAsync(new Uri($"https://{_options.TeamDomain}/cdn-cgi/access/certs"), cancellationToken);
                // The certs document is a JWKS ("keys") plus PEM copies of the same keys, which are ignored.
                _keys = new JsonWebKeySet(json).GetSigningKeys().ToList();
                _keysFetchedAtUtc = DateTime.UtcNow;
            }
        }
        finally
        {
            _refresh.Release();
        }

        return _keys;
    }
}

/// <summary>Refuses internal tool paths (Hangfire dashboard, Swagger) unless the request came through Cloudflare Access.</summary>
public sealed class CloudflareAccessGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IOptions<CloudflareAccessOptions> options, CloudflareAccessValidator validator)
    {
        var settings = options.Value;
        if (settings.Enabled && settings.ProtectedPaths.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase))
            && !await validator.IsValidAsync(context.Request.Headers[CloudflareAccessValidator.AssertionHeader], context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }
}
