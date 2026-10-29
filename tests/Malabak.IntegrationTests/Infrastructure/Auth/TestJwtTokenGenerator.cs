using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Malabak.IntegrationTests.Infrastructure.Auth;

public static class TestJwtTokenGenerator
{
    public static string GenerateToken(
        IServiceProvider services,
        string role,
        Guid? userId = null,
        string? phoneNumber = null,
        IDictionary<string, string>? customClaims = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var keyProvider = services.GetRequiredService<IJwtKeyProvider>();
        var jwtOptions = services.GetRequiredService<IOptions<JwtOptions>>().Value;

        return GenerateToken(
            keyProvider.GetCurrentSigningCredentials(),
            jwtOptions.Issuer,
            jwtOptions.Audience,
            role,
            userId,
            phoneNumber,
            customClaims);
    }

    public static string GenerateToken(
        SigningCredentials credentials,
        string issuer,
        string audience,
        string role,
        Guid? userId = null,
        string? phoneNumber = null,
        IDictionary<string, string>? customClaims = null)
    {
        var targetUserId = userId ?? Guid.NewGuid();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, targetUserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.Role, role),
            new("role", role),
            new("security_stamp", Guid.NewGuid().ToString("N")),
            new("phone_number", phoneNumber ?? "+201012345678")
        };

        if (customClaims != null)
        {
            foreach (var kvp in customClaims)
            {
                claims.Add(new Claim(kvp.Key, kvp.Value));
            }
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(60),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = credentials
        };

        var handler = new JwtSecurityTokenHandler();
        var securityToken = handler.CreateToken(tokenDescriptor);
        return handler.WriteToken(securityToken);
    }
}
