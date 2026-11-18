using Microsoft.AspNetCore.Http;

namespace Malabak.Api.Infrastructure.Security;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            if (context.Request.IsHttps)
            {
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
            }
            headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'self'";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "geolocation=(self), camera=(), microphone=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";

            // API responses must never be cached by shared caches or browsers, unless the endpoint opted in
            // explicitly for non-personal data (e.g. public reference data with an ETag).
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                && !headers.ContainsKey("Cache-Control"))
            {
                headers["Cache-Control"] = "no-store, no-cache, max-age=0, must-revalidate";
                headers["Pragma"] = "no-cache";
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
