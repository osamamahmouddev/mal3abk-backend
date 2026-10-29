using System.Security.Claims;
using Malabak.Modules.Identity.Domain;

namespace Malabak.Modules.Identity.Application.Common;

public interface ITokenService
{
    /// <param name="sessionId">Device session id, emitted as the <c>sid</c> claim and checked on every request.</param>
    TokenResult GenerateTokens(User user, Guid? sessionId = null);

    /// <summary>Admin-audience tokens; the refresh token expires at the fixed end of the admin session.</summary>
    TokenResult GenerateAdminTokens(User user, DateTime sessionExpiresAtUtc, Guid? sessionId = null);

    string GenerateRawRefreshToken();

    string HashRefreshToken(string rawRefreshToken);

    ClaimsPrincipal? ValidateAccessToken(string token);
}
