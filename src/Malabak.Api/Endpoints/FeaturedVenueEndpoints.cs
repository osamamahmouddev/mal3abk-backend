using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Venues.Application.Featured;
using Malabak.Modules.Venues.Application.Home;
using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>Featured venues and promotions (TASK-711): admin scheduling and the public, flag-gated featured list.</summary>
public static class FeaturedVenueEndpoints
{
    public static void MapFeaturedVenueEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}").WithApiVersionSet(apiVersionSet).MapToApiVersion(1, 0);

        v1.MapGet("/venues/featured", async (ClaimsPrincipal user, HttpContext http, FeaturedVenueService service, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "private, max-age=60";
                return Results.Ok(await service.LiveAsync(FeaturedPlacementSlot.Explore, user.GetUserId(), ct));
            })
            .AllowAnonymous()
            .RequireDistributedRateLimiting("featured-venues", permitLimit: 120, windowSeconds: 60)
            .Produces<IReadOnlyList<SponsoredItem>>()
            .WithTags("Explore")
            .WithName("GetFeaturedVenues")
            .WithSummary("Featured (sponsored) venues for explore; empty while the venues.featured flag is off. Always show the sponsored label.");

        var admin = v1.MapGroup("/admin/featured-venues").RequireAuthorization(Permissions.AdminSettingsManage)
            .AddEndpointFilter<AdminIpAllowlistFilter>().WithTags("Admin Featured Venues");

        admin.MapGet("", async ([FromQuery] bool? includeEnded, HttpContext http, FeaturedVenueService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return Results.Ok(await service.ListAsync(includeEnded ?? false, ct));
            })
            .Produces<IReadOnlyList<FeaturedPlacementDto>>()
            .WithName("AdminListFeaturedPlacements").WithSummary("Scheduled and live placements (add includeEnded=true for history)");

        admin.MapPost("", async ([FromBody] FeaturedPlacementApiRequest request, ClaimsPrincipal user, FeaturedVenueService service, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(request.ToInput(), user.GetUserId()!.Value, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/admin/featured-venues/{result.Value.Id}", result.Value) : AdminResults.Problem(result.Error);
            })
            .Produces<FeaturedPlacementDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminCreateFeaturedPlacement").WithSummary("Schedule a live venue in the Home or Explore slot (max 90 days, 10 at a time per slot)");

        admin.MapPut("/{placementId:guid}", async (Guid placementId, [FromBody] FeaturedPlacementApiRequest request, ClaimsPrincipal user, FeaturedVenueService service, CancellationToken ct) =>
                AdminResults.From(await service.UpdateAsync(placementId, request.ToInput(), user.GetUserId()!.Value, ct)))
            .Produces<FeaturedPlacementDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminUpdateFeaturedPlacement").WithSummary("Change slot, title, schedule or order");

        admin.MapPost("/{placementId:guid}/cancel", async (Guid placementId, ClaimsPrincipal user, FeaturedVenueService service, CancellationToken ct) =>
                AdminResults.From(await service.CancelAsync(placementId, user.GetUserId()!.Value, ct)))
            .Produces<FeaturedPlacementDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminCancelFeaturedPlacement").WithSummary("Stop a placement now (kept for history)");
    }
}

public sealed record FeaturedPlacementApiRequest(Guid VenueId, string? Slot, string? Title, DateTime StartsAtUtc, DateTime EndsAtUtc, int SortOrder)
{
    public FeaturedPlacementInput ToInput() => new(VenueId, Slot, Title, StartsAtUtc, EndsAtUtc, SortOrder);
}
