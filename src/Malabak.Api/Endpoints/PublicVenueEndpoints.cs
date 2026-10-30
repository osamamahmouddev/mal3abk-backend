using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Venues.Application.Availability;
using Malabak.Modules.Venues.Application.Explore;
using Malabak.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>Player-facing venue endpoints (no login needed to browse): availability, explore and map.</summary>
public static class PublicVenueEndpoints
{
    public static void MapPublicVenueEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var venues = app.MapGroup("/api/v{version:apiVersion}/venues")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .AllowAnonymous()
            .WithTags("Venues (public)");

        venues.MapGet("/{venueId:guid}/availability", async (Guid venueId, [FromQuery] DateOnly date, [FromQuery] Guid? pitchId,
            HttpContext http, CachedVenueAvailability availability, CancellationToken ct) =>
        {
            var result = await availability.GetAsync(venueId, date, pitchId, ct);
            if (result.IsFailure)
            {
                return ToResult(result);
            }

            // Short private caching: clients revalidate with If-None-Match and get 304 while nothing changed.
            http.Response.Headers.ETag = result.Value.ETag;
            http.Response.Headers.CacheControl = "private, max-age=0, must-revalidate";
            return http.Request.Headers.IfNoneMatch.ToString() == result.Value.ETag
                ? Results.StatusCode(StatusCodes.Status304NotModified)
                : Results.Ok(result.Value.Availability);
        })
        .RequireDistributedRateLimiting("public-availability", permitLimit: 120, windowSeconds: 60)
        .WithName("GetVenueAvailability")
        .WithSummary("Slots per pitch for a business date with state (available, held, booked, blocked, unavailable) and price");

        // Anonymous browsing; a signed-in player also gets the favorite flag on each card.
        // Location is used for this request only: responses that depend on it are never cached by browsers or proxies.
        venues.MapGet("/explore", async ([AsParameters] ExploreApiQuery query, ClaimsPrincipal user, HttpContext http, VenueExplore explore, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return ToResult(await explore.SearchAsync(query.ToQuery(), user.GetUserId(), ct));
        })
        .RequireDistributedRateLimiting("public-explore", permitLimit: 60, windowSeconds: 60)
        .WithName("ExploreVenues")
        .WithSummary("Search approved venues by location, area, date and time, budget, pitch type, surface and facilities");

        venues.MapGet("/map", async ([AsParameters] ExploreApiQuery query, [FromQuery] double south, [FromQuery] double west,
            [FromQuery] double north, [FromQuery] double east, ClaimsPrincipal user, HttpContext http, VenueExplore explore, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return ToResult(await explore.MapAsync(query.ToQuery() with { Bounds = new MapBounds(south, west, north, east) }, user.GetUserId(), ct));
        })
        .RequireDistributedRateLimiting("public-map", permitLimit: 60, windowSeconds: 60)
        .WithName("MapVenues")
        .WithSummary("Venue pins inside the visible map bounds (max 2 degrees wide) with the explore filters");

        venues.MapGet("/areas", async (HttpContext http, VenueExplore explore, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "public, max-age=300";
            return Results.Ok(await explore.AreasAsync(ct));
        })
        .RequireDistributedRateLimiting("public-areas", permitLimit: 60, windowSeconds: 60)
        .WithName("ExploreAreas")
        .WithSummary("Cities and areas that have venues, for choosing a place by hand when location is not shared");
    }

    private static IResult ToResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        var status = result.Error.Code.EndsWith("NotFound", StringComparison.Ordinal) ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
        return Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: status);
    }
}

/// <summary>Query string for GET /venues/explore. <c>facilities</c> is comma separated; times are Cairo local (HH:mm).</summary>
public sealed record ExploreApiQuery(
    [FromQuery] double? Lat,
    [FromQuery] double? Lng,
    [FromQuery] double? RadiusKm,
    [FromQuery] string? City,
    [FromQuery] string? Area,
    [FromQuery] DateOnly? Date,
    [FromQuery] TimeOnly? From,
    [FromQuery] TimeOnly? To,
    [FromQuery] decimal? MaxPrice,
    [FromQuery] string? PitchType,
    [FromQuery] string? Surface,
    [FromQuery] string? Facilities,
    [FromQuery] ExploreSort? Sort,
    [FromQuery] int? Page,
    [FromQuery] int? PageSize)
{
    public ExploreQuery ToQuery() => new(
        Lat, Lng, RadiusKm, City, Area, Date, From, To, MaxPrice, PitchType, Surface,
        Facilities?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        Sort ?? ExploreSort.Distance, Page ?? 1, PageSize ?? 20);
}
