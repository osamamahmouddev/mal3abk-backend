using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Identity.Application.Authorization;
using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Venues.Application.Common;

namespace Malabak.Api.Endpoints;

public static class MeEndpoints
{
    public static void MapMeEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/me")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(MalabakAuthorization.AnySessionPolicy)
            .WithTags("Me");

        group.MapGet("/permissions", async (
            ClaimsPrincipal user,
            IUserPermissionService permissionService,
            IVenueAccessService venueAccessService,
            CancellationToken cancellationToken) =>
        {
            var userId = user.GetUserId();
            if (!userId.HasValue)
            {
                return Results.Unauthorized();
            }

            var permissions = await permissionService.GetAsync(userId.Value, cancellationToken);
            if (permissions is null)
            {
                return Results.Unauthorized();
            }

            var venues = await venueAccessService.GetAccessibleVenuesAsync(userId.Value, cancellationToken);

            // Informational only: the UI uses this to show/hide actions, the API still enforces every one.
            return Results.Ok(new MyPermissionsResponse(
                permissions.Roles.Order(StringComparer.Ordinal).ToList(),
                permissions.Permissions.Order(StringComparer.Ordinal).ToList(),
                venues.Select(v => new VenuePermissionsResponse(
                    v.VenueId,
                    v.Level.ToString(),
                    v.Permissions.Order(StringComparer.Ordinal).ToList())).ToList()));
        })
        .Produces<MyPermissionsResponse>().WithName("GetMyPermissions")
        .WithSummary("Get the current user's roles, platform permissions and per-venue permissions");

        // --- Device sessions ("signed-in devices" screen) ---

        group.MapGet("/sessions", async (ClaimsPrincipal user, SessionService sessions, CancellationToken cancellationToken) =>
            Results.Ok(await sessions.ListAsync(user.GetUserId()!.Value, user.GetSessionId(), cancellationToken)))
        .Produces<IReadOnlyList<SessionDto>>().WithName("GetMySessions")
        .WithSummary("Active devices signed in to this account (the current one is flagged)");

        group.MapDelete("/sessions/{sessionId:guid}", async (Guid sessionId, ClaimsPrincipal user, SessionService sessions,
            CancellationToken cancellationToken) =>
            await sessions.RevokeAsync(user.GetUserId()!.Value, sessionId, "user_revoked", cancellationToken)
                ? Results.NoContent()
                : Results.Problem(title: "Identity.SessionNotFound", statusCode: StatusCodes.Status404NotFound))
        .WithName("RevokeMySession")
        .WithSummary("Sign out one device");

        group.MapPost("/sessions/revoke-others", async (ClaimsPrincipal user, SessionService sessions, CancellationToken cancellationToken) =>
        {
            var revoked = await sessions.RevokeAllAsync(user.GetUserId()!.Value, user.GetSessionId(), "user_revoked_others", cancellationToken);
            return Results.Ok(new { Revoked = revoked });
        })
        .WithName("RevokeOtherSessions")
        .WithSummary("Sign out every device except this one");
    }

    public sealed record MyPermissionsResponse(
        IReadOnlyList<string> Roles,
        IReadOnlyList<string> Permissions,
        IReadOnlyList<VenuePermissionsResponse> Venues);

    public sealed record VenuePermissionsResponse(
        Guid VenueId,
        string AccessLevel,
        IReadOnlyList<string> Permissions);
}
