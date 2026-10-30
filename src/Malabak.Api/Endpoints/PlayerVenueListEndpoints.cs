using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Venues.Application.PlayerLists;
using Malabak.SharedKernel;

namespace Malabak.Api.Endpoints;

/// <summary>Favorites and recently viewed venues for the signed-in player (own data only; ids come from the token).</summary>
public static class PlayerVenueListEndpoints
{
    public static void MapPlayerVenueListEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var me = app.MapGroup("/api/v{version:apiVersion}/me")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization()
            .WithTags("Favorites & Recently Viewed");

        me.MapGet("/favorites", async (ClaimsPrincipal user, PlayerVenueListHandlers lists, CancellationToken ct) =>
            Results.Ok(await lists.GetFavoritesAsync(user.GetUserId()!.Value, ct)))
        .Produces<IReadOnlyList<VenueCardDto>>().WithName("GetFavoriteVenues").WithSummary("Favorite venues with a next-available-slot hint");

        me.MapPut("/favorites/{venueId:guid}", async (Guid venueId, ClaimsPrincipal user, PlayerVenueListHandlers lists, CancellationToken ct) =>
            ToResult(await lists.AddFavoriteAsync(user.GetUserId()!.Value, venueId, ct)))
        .WithName("AddFavoriteVenue");

        me.MapDelete("/favorites/{venueId:guid}", async (Guid venueId, ClaimsPrincipal user, PlayerVenueListHandlers lists, CancellationToken ct) =>
        {
            await lists.RemoveFavoriteAsync(user.GetUserId()!.Value, venueId, ct);
            return Results.NoContent();
        })
        .WithName("RemoveFavoriteVenue");

        me.MapGet("/recently-viewed", async (ClaimsPrincipal user, PlayerVenueListHandlers lists, CancellationToken ct) =>
            Results.Ok(await lists.GetRecentlyViewedAsync(user.GetUserId()!.Value, ct)))
        .Produces<IReadOnlyList<VenueCardDto>>().WithName("GetRecentlyViewedVenues").WithSummary("Up to 20 most recently viewed venues");

        me.MapPost("/recently-viewed/{venueId:guid}", async (Guid venueId, ClaimsPrincipal user, PlayerVenueListHandlers lists, CancellationToken ct) =>
            ToResult(await lists.RecordViewAsync(user.GetUserId()!.Value, venueId, ct)))
        .WithName("RecordVenueView").WithSummary("Called when the player opens a venue's details");

        me.MapDelete("/recently-viewed", async (ClaimsPrincipal user, PlayerVenueListHandlers lists, CancellationToken ct) =>
        {
            await lists.ClearRecentlyViewedAsync(user.GetUserId()!.Value, ct);
            return Results.NoContent();
        })
        .WithName("ClearRecentlyViewedVenues");
    }

    private static IResult ToResult(Result result)
    {
        if (result.IsSuccess)
        {
            return Results.NoContent();
        }

        var status = result.Error == PlayerListErrors.VenueNotFound ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict;
        return Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: status);
    }
}
