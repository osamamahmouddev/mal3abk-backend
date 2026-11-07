using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.SharedKernel;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Malabak.Modules.Identity.Infrastructure.Services;

public sealed class TokenService : ITokenService
{
    public const string SessionClaim = "sid";

    private readonly IJwtKeyProvider _keyProvider;
    private readonly IClock _clock;
    private readonly JwtOptions _options;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public TokenService(
        IJwtKeyProvider keyProvider,
        IClock clock,
        IOptions<JwtOptions> options)
    {
        _keyProvider = keyProvider;
        _clock = clock;
        _options = options.Value;
    }

    public TokenResult GenerateTokens(User user, Guid? sessionId = null)
    {
        var nowUtc = _clock.UtcNow;
        var expirationMinutes = _options.AccessTokenExpirationMinutes > 0 ? _options.AccessTokenExpirationMinutes : 15;
        var refreshTokenDays = _options.RefreshTokenExpirationDays > 0 ? _options.RefreshTokenExpirationDays : 7;

        return Issue(user, _options.Audience, TimeSpan.FromMinutes(expirationMinutes), nowUtc.AddDays(refreshTokenDays), amr: null, sessionId);
    }

    public TokenResult GenerateAdminTokens(User user, DateTime sessionExpiresAtUtc, Guid? sessionId = null)
    {
        var minutes = _options.AdminAccessTokenExpirationMinutes > 0 ? _options.AdminAccessTokenExpirationMinutes : 5;
        return Issue(user, _options.AdminAudience, TimeSpan.FromMinutes(minutes), sessionExpiresAtUtc, amr: ["pwd", "mfa"], sessionId);
    }

    public TokenResult GenerateImpersonationToken(User user, Guid sessionId, Guid actorId, TimeSpan lifetime) =>
        Issue(user, _options.Audience, lifetime, _clock.UtcNow + lifetime, amr: ["imp"], sessionId,
            new Claim(ImpersonationActorClaim, $"{{\"sub\":\"{actorId:D}\"}}", JsonClaimValueTypes.Json));

    /// <summary>RFC 8693 actor claim: who is acting on behalf of the subject.</summary>
    public const string ImpersonationActorClaim = "act";

    // The payload is readable by anyone (base64), so no PII goes in: no phone, no email (SECURITY.md §3.3).
    private TokenResult Issue(
        User user, string audience, TimeSpan accessLifetime, DateTime refreshExpiresAtUtc, string[]? amr, Guid? sessionId, Claim? extra = null)
    {
        ArgumentNullException.ThrowIfNull(user);

        var nowUtc = _clock.UtcNow;
        var expiresAtUtc = nowUtc + accessLifetime;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(nowUtc).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("security_stamp", user.SecurityStamp),
            new("name", user.FullName)
        };

        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange((amr ?? []).Select(method => new Claim("amr", method)));

        if (sessionId is { } sid)
        {
            claims.Add(new Claim(SessionClaim, sid.ToString("D")));
        }

        if (extra is not null)
        {
            claims.Add(extra);
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = audience,
            NotBefore = nowUtc,
            IssuedAt = nowUtc,
            Expires = expiresAtUtc,
            SigningCredentials = _keyProvider.GetCurrentSigningCredentials()
        };

        var accessToken = _tokenHandler.WriteToken(_tokenHandler.CreateToken(tokenDescriptor));

        return new TokenResult(
            AccessToken: accessToken,
            ExpiresIn: (int)accessLifetime.TotalSeconds,
            RefreshToken: GenerateRawRefreshToken(),
            RefreshTokenExpiresAtUtc: refreshExpiresAtUtc);
    }

    public string GenerateRawRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(randomBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public string HashRefreshToken(string rawRefreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawRefreshToken);

        var bytes = Encoding.UTF8.GetBytes(rawRefreshToken.Trim());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = _keyProvider.GetValidationKeys(),
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var now = _clock.UtcNow;
                return (!notBefore.HasValue || notBefore.Value <= now.AddMinutes(1)) &&
                       (!expires.HasValue || expires.Value >= now.AddMinutes(-1));
            }
        };

        try
        {
            return _tokenHandler.ValidateToken(token, validationParameters, out _);
        }
        catch
        {
            return null;
        }
    }
}
