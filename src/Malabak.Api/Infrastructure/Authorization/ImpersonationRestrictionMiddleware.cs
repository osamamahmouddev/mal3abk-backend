using Malabak.Modules.Identity.Infrastructure.Services;

namespace Malabak.Api.Infrastructure.Authorization;

/// <summary>
/// Blocks sensitive actions for impersonation tokens (TASK-063, SECURITY.md): anything under <c>/auth</c> (passwords, 2FA,
/// step-up, sign-out everywhere), phone change, data export, account deletion, consent changes, revoking sessions, payment
/// (bank/wallet) details, and every admin endpoint. Runs after authentication, so it sees the token's <c>act</c> claim.
/// </summary>
public sealed class ImpersonationRestrictionMiddleware(RequestDelegate next)
{
    private static readonly string[] _blockedPrefixes = ["/auth", "/me/phone-change", "/me/data-export", "/admin"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.HasClaim(c => c.Type == TokenService.ImpersonationActorClaim) && IsRestricted(context.Request.Method, context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Impersonation.Restricted",
                detail = "This action is not available while support is signed in to the account.",
                status = StatusCodes.Status403Forbidden
            });
            return;
        }

        await next(context);
    }

    public static bool IsRestricted(string method, PathString path)
    {
        var relative = Relative(path.Value ?? string.Empty);
        if (relative is null)
        {
            return false;
        }

        if (_blockedPrefixes.Any(p => relative.Equals(p, StringComparison.OrdinalIgnoreCase) || relative.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (relative.Contains("/payment-methods", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var write = !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method);
        return write && (relative.Equals("/me", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("/me/sessions", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("/me/consents", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"/api/v1/me/sessions" → "/me/sessions"; null for paths outside the versioned API.</summary>
    private static string? Relative(string path)
    {
        const string api = "/api/v";
        if (!path.StartsWith(api, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var slash = path.IndexOf('/', api.Length);
        return slash < 0 ? "/" : path[slash..].TrimEnd('/');
    }
}
