using System.Security.Claims;
using Malabak.Modules.Identity.Domain;

namespace Malabak.Modules.Identity.Application.Common;

public interface ITokenService
{
    TokenResult GenerateTokens(User user);

    /// <summary>Admin-audience tokens; the refresh token expires at the fixed end of the admin session.</summary>
    TokenResult GenerateAdminTokens(User user, DateTime sessionExpiresAtUtc);

    string GenerateRawRefreshToken();

    string HashRefreshToken(string rawRefreshToken);

    ClaimsPrincipal? ValidateAccessToken(string token);
}
