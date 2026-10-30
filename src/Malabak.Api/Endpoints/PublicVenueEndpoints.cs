using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Venues.Application.Availability;
using Malabak.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>Player-facing venue endpoints (no login needed to browse): availability, and later explore and details.</summary>
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
            VenueAvailabilityQuery availability, CancellationToken ct) =>
            ToResult(await availability.GetAsync(venueId, date, pitchId, ct)))
        .RequireDistributedRateLimiting("public-availability", permitLimit: 120, windowSeconds: 60)
        .WithName("GetVenueAvailability")
        .WithSummary("Slots per pitch for a business date with state (available, held, booked, blocked, unavailable) and price");
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
