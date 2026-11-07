using Malabak.SharedKernel.Contracts;

namespace Malabak.Api.Infrastructure.Platform;

/// <summary>
/// Maintenance mode (TASK-712): while on, API calls answer 503 with the admin's message. Admin routes, the admin sign-in,
/// the public status endpoint, health checks and Swagger keep working so the platform can always be switched back on.
/// </summary>
public sealed class MaintenanceModeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IPlatformSettings settings)
    {
        if (IsExempt(context.Request.Path) || !await settings.GetBoolAsync(PlatformSettingKeys.MaintenanceEnabled, context.RequestAborted))
        {
            await next(context);
            return;
        }

        var message = await settings.GetStringAsync(PlatformSettingKeys.MaintenanceMessage, context.RequestAborted);
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "300";
        await Results.Problem(title: "Platform.Maintenance", detail: message, statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(context);
    }

    public static bool IsExempt(PathString path)
    {
        var value = path.Value ?? string.Empty;
        if (!value.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase))
        {
            return true; // health, swagger, hangfire and anything outside the API
        }

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // /api/v1/<area>/...
        var area = segments.Length > 2 ? segments[2] : string.Empty;
        return area.Equals("admin", StringComparison.OrdinalIgnoreCase)
            || (area.Equals("platform", StringComparison.OrdinalIgnoreCase) && segments.Length > 3 && segments[3].Equals("status", StringComparison.OrdinalIgnoreCase));
    }
}
