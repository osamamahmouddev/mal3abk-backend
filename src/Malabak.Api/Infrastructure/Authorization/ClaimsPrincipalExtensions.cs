using System.Security.Claims;

namespace Malabak.Api.Infrastructure.Authorization;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var claimValue = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;

        return Guid.TryParse(claimValue, out var userId) ? userId : null;
    }

    /// <summary>Device session id (<c>sid</c>; the JWT handler may map it to <see cref="ClaimTypes.Sid"/>).</summary>
    public static Guid? GetSessionId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var claimValue = principal.FindFirst("sid")?.Value ?? principal.FindFirst(ClaimTypes.Sid)?.Value;
        return Guid.TryParse(claimValue, out var sessionId) ? sessionId : null;
    }
}
