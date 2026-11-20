using System.Security.Claims;
using Malabak.Modules.Identity.Domain;

namespace Malabak.Modules.Identity.Application.Common;

public interface ITokenService
{
    /// <param name="sessionId">Device session id, emitted as the <c>sid</c> claim and checked on every request.</param>
    TokenResult GenerateTokens(User user, Guid? sessionId = null);

    /// <summary>Admin-audience tokens; the refresh token expires at the fixed end of the admin session.</summary>
    /// <param name="secondFactor">Whether the sign-in used the authenticator; sets the token's <c>amr</c> to pwd+mfa or pwd.</param>
    TokenResult GenerateAdminTokens(User user, DateTime sessionExpiresAtUtc, Guid? sessionId = null, bool secondFactor = true);

    /// <summary>
    /// Short-lived access token for support impersonation (TASK-063): issued for the customer, carrying the RFC 8693
    /// <c>act</c> claim with the staff member's id. No refresh token is meant to be used with it.
    /// </summary>
    TokenResult GenerateImpersonationToken(User user, Guid sessionId, Guid actorId, TimeSpan lifetime);

    string GenerateRawRefreshToken();

    string HashRefreshToken(string rawRefreshToken);

    ClaimsPrincipal? ValidateAccessToken(string token);
}
