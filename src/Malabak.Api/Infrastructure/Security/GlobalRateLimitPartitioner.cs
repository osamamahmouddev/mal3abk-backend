using System.Security.Claims;
using System.Threading.RateLimiting;
using Malabak.Api.Infrastructure.Configuration;

namespace Malabak.Api.Infrastructure.Security;

/// <summary>
/// The global request limit. Signed-in callers get their own bucket keyed by user id: mobile carriers put many players
/// behind one public IP (CGNAT), so an IP bucket would make them throttle each other. Anonymous traffic stays per IP.
/// It runs after authentication, so the user is known; an invalid or missing token simply counts as anonymous.
/// </summary>
public static class GlobalRateLimitPartitioner
{
    public static RateLimitPartition<string> GetPartition(HttpContext context, RateLimiterOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var userId = context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirstValue("sub") ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;

        var key = userId is not null
            ? $"user:{userId}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = options.PermitLimit,
            QueueLimit = options.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            Window = TimeSpan.FromSeconds(options.WindowSeconds),
        });
    }
}
