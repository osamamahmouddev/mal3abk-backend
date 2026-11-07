using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Platform;
using Malabak.Api.Infrastructure.Security;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>Platform settings and feature flags (TASK-712): admin management plus the public status clients poll.</summary>
public static class PlatformEndpoints
{
    public static void MapPlatformEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        app.MapGet("/api/v{version:apiVersion}/platform/status", async (HttpContext http, PlatformService platform, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "public, max-age=30";
            return Results.Ok(await platform.StatusAsync(ct));
        })
        .WithApiVersionSet(apiVersionSet)
        .MapToApiVersion(1, 0)
        .AllowAnonymous()
        .RequireDistributedRateLimiting("platform-status", permitLimit: 120, windowSeconds: 60)
        .Produces<PlatformStatusDto>()
        .WithTags("Platform")
        .WithName("GetPlatformStatus")
        .WithSummary("Maintenance state and enabled public features (apps check this on start)");

        var admin = app.MapGroup("/api/v{version:apiVersion}/admin")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.AdminSettingsManage)
            .WithTags("Admin Platform Settings");

        admin.MapGet("/settings", async (PlatformService platform, CancellationToken ct) => Results.Ok(await platform.ListSettingsAsync(ct)))
            .Produces<IReadOnlyList<SettingDto>>()
            .WithName("ListPlatformSettings")
            .WithSummary("Every typed setting with its current value, default and allowed range");

        admin.MapPut("/settings/{key}", async (string key, [FromBody] UpdateSettingApiRequest request, ClaimsPrincipal user, PlatformService platform, CancellationToken ct) =>
            ToResult(await platform.UpdateSettingAsync(key, request.Value, user.GetUserId()!.Value, request.Reason, ct)))
            .RequireDistributedRateLimiting("admin-settings-write", permitLimit: 30, windowSeconds: 60)
            .Produces<SettingDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("UpdatePlatformSetting")
            .WithSummary("Change a setting (validated against its type and range; audited)");

        admin.MapGet("/feature-flags", async (PlatformService platform, CancellationToken ct) => Results.Ok(await platform.ListFlagsAsync(ct)))
            .Produces<IReadOnlyList<FlagDto>>()
            .WithName("ListFeatureFlags")
            .WithSummary("Every known feature flag and whether it is on");

        admin.MapPut("/feature-flags/{key}", async (string key, [FromBody] UpdateFlagApiRequest request, ClaimsPrincipal user, PlatformService platform, CancellationToken ct) =>
            ToResult(await platform.UpdateFlagAsync(key, request.Enabled, user.GetUserId()!.Value, request.Reason, ct)))
            .RequireDistributedRateLimiting("admin-settings-write", permitLimit: 30, windowSeconds: 60)
            .Produces<FlagDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("UpdateFeatureFlag")
            .WithSummary("Turn a feature on or off (audited)");
    }

    private static IResult ToResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        var status = result.Error.Code.StartsWith("Platform.Unknown", StringComparison.Ordinal) ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
        return Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: status);
    }
}

public sealed record UpdateSettingApiRequest(string Value, string? Reason = null);

public sealed record UpdateFlagApiRequest(bool Enabled, string? Reason = null);
