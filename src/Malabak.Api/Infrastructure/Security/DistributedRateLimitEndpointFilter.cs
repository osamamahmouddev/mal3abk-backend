using System.Diagnostics;
using System.Security.Claims;
using Malabak.Api.Infrastructure.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Api.Infrastructure.Security;

public sealed class DistributedRateLimitEndpointFilter : IEndpointFilter
{
    private readonly IDistributedRateLimiter _rateLimiter;
    private readonly DistributedRateLimitAttribute _attribute;

    public DistributedRateLimitEndpointFilter(
        IDistributedRateLimiter rateLimiter,
        DistributedRateLimitAttribute attribute)
    {
        _rateLimiter = rateLimiter;
        _attribute = attribute;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;
        var clientKey = ResolveClientKey(httpContext);

        RateLimitResult result;
        var window = TimeSpan.FromSeconds(_attribute.WindowSeconds);

        switch (_attribute.Algorithm)
        {
            case RateLimitAlgorithm.FixedWindow:
                result = await _rateLimiter.CheckFixedWindowAsync(
                    _attribute.Policy,
                    clientKey,
                    _attribute.PermitLimit,
                    window,
                    _attribute.IsSensitive,
                    httpContext.RequestAborted);
                break;

            case RateLimitAlgorithm.TokenBucket:
                result = await _rateLimiter.CheckTokenBucketAsync(
                    _attribute.Policy,
                    clientKey,
                    _attribute.PermitLimit,
                    1,
                    TimeSpan.FromSeconds((double)_attribute.WindowSeconds / _attribute.PermitLimit),
                    _attribute.IsSensitive,
                    httpContext.RequestAborted);
                break;

            default:
                result = await _rateLimiter.CheckSlidingWindowAsync(
                    _attribute.Policy,
                    clientKey,
                    _attribute.PermitLimit,
                    window,
                    _attribute.IsSensitive,
                    httpContext.RequestAborted);
                break;
        }

        if (!result.IsAllowed)
        {
            var retryAfterSec = Math.Max(1, (int)result.RetryAfter.TotalSeconds);
            httpContext.Response.Headers.RetryAfter = retryAfterSec.ToString();
            httpContext.Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString();
            httpContext.Response.Headers["X-RateLimit-Remaining"] = "0";
            httpContext.Response.Headers["X-RateLimit-Reset"] = retryAfterSec.ToString();

            var localizer = httpContext.RequestServices.GetService<IErrorMessageLocalizer>();
            var title = localizer?.GetLocalizedTitle("TooManyRequests") ?? "Too Many Requests";
            var detail = localizer?.GetLocalizedMessage("rate_limited", "Too many requests. Please try again later.")
                ?? "Too many requests. Please try again later.";
            var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

            return Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: title,
                detail: detail,
                type: "https://tools.ietf.org/html/rfc9110#section-15.5.20",
                instance: httpContext.Request.Path,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "rate_limited",
                    ["traceId"] = traceId
                });
        }

        httpContext.Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString();
        httpContext.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();

        return await next(context);
    }

    private static string ResolveClientKey(HttpContext context)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip";
        return $"ip:{ip}";
    }
}
