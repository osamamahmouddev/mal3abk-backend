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

    public TokenResult GenerateTokens(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var nowUtc = _clock.UtcNow;
        var expirationMinutes = _options.AccessTokenExpirationMinutes > 0 ? _options.AccessTokenExpirationMinutes : 15;
        var expiresAtUtc = nowUtc.AddMinutes(expirationMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(nowUtc).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("phone_number", user.PhoneNumber),
            new("security_stamp", user.SecurityStamp),
            new("name", user.FullName)
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new(JwtRegisteredClaimNames.Email, user.Email));
        }

        foreach (var role in user.Roles)
        {
            claims.Add(new(ClaimTypes.Role, role));
        }

        var signingCredentials = _keyProvider.GetCurrentSigningCredentials();

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            NotBefore = nowUtc,
            IssuedAt = nowUtc,
            Expires = expiresAtUtc,
            SigningCredentials = signingCredentials
        };

        var securityToken = _tokenHandler.CreateToken(tokenDescriptor);
        var accessToken = _tokenHandler.WriteToken(securityToken);

        var rawRefreshToken = GenerateRawRefreshToken();
        var refreshTokenDays = _options.RefreshTokenExpirationDays > 0 ? _options.RefreshTokenExpirationDays : 7;
        var refreshTokenExpiresAtUtc = nowUtc.AddDays(refreshTokenDays);

        var expiresInSeconds = (int)(expiresAtUtc - nowUtc).TotalSeconds;

        return new TokenResult(
            AccessToken: accessToken,
            ExpiresIn: expiresInSeconds,
            RefreshToken: rawRefreshToken,
            RefreshTokenExpiresAtUtc: refreshTokenExpiresAtUtc);
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
