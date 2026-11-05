using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Application.Matches.CancelMatch;
using Malabak.Modules.Matches.Application.Matches.GetDisputedMatches;
using Malabak.Modules.Matches.Application.Matches.GetMatchById;
using Malabak.Modules.Matches.Application.Matches.GetMatchHistory;
using Malabak.Modules.Matches.Application.Matches.GetUpcomingMatches;
using Malabak.Modules.Matches.Application.Matches.LinkBooking;
using Malabak.Modules.Matches.Application.Matches.ScheduleMatch;
using Malabak.Modules.Matches.Application.Matches.SubmitResult;
using Malabak.Modules.Matches.Application.Matches.VerifyResult;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class MatchEndpoints
{
    public static void MapMatchEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var matches = app.MapGroup("/api/v{version:apiVersion}/matches")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.TeamsParticipate)
            .WithTags("Matches");

        // 1. Get upcoming matches
        matches.MapGet("/upcoming", async (
            [FromQuery] Guid? teamId,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            IQueryHandler<GetUpcomingMatchesQuery, Result<PagedList<MatchDto>>> handler,
            CancellationToken ct) =>
        {
            var query = new GetUpcomingMatchesQuery(teamId, page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize);
            var result = await handler.HandleAsync(query, ct);
            return ToResult(result);
        })
        .WithName("GetUpcomingMatches")
        .WithSummary("Get upcoming scheduled matches, optionally filtered by team");

        // 2. Get match history
        matches.MapGet("/history", async (
            [FromQuery] Guid? teamId,
            [FromQuery] MatchStatus? status,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            IQueryHandler<GetMatchHistoryQuery, Result<PagedList<MatchDto>>> handler,
            CancellationToken ct) =>
        {
            var query = new GetMatchHistoryQuery(teamId, status, page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize);
            var result = await handler.HandleAsync(query, ct);
            return ToResult(result);
        })
        .WithName("GetMatchHistory")
        .WithSummary("Get completed, disputed, or cancelled match history");

        // 3. Get match by ID
        matches.MapGet("/{id:guid}", async (
            Guid id,
            IQueryHandler<GetMatchByIdQuery, Result<MatchDto>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetMatchByIdQuery(id), ct);
            return ToResult(result);
        })
        .WithName("GetMatchById")
        .WithSummary("Get details of a specific match by ID");

        // 4. Schedule match directly
        matches.MapPost("/schedule", async (
            [FromQuery] Guid homeTeamId,
            ClaimsPrincipal user,
            [FromBody] ScheduleMatchRequest request,
            ICommandHandler<ScheduleMatchCommand, Result<MatchDto>> handler,
            CancellationToken ct) =>
        {
            var callerId = user.GetUserId()!.Value;
            var command = new ScheduleMatchCommand(
                homeTeamId,
                request.AwayTeamId,
                request.ScheduledStartUtc,
                request.DurationMinutes,
                request.PitchId,
                request.VenueId,
                request.VenueName,
                request.BookingId,
                request.Notes,
                callerId);

            var result = await handler.HandleAsync(command, ct);
            return ToResult(result, StatusCodes.Status201Created, result.IsSuccess ? $"/api/v1/matches/{result.Value.Id}" : null);
        })
        .WithName("ScheduleMatch")
        .WithSummary("Directly schedule a match against another team (Captain only)");

        // 5. Cancel scheduled match
        matches.MapPost("/{id:guid}/cancel", async (
            Guid id,
            [FromQuery] Guid teamId,
            ClaimsPrincipal user,
            [FromBody] CancelMatchRequest request,
            ICommandHandler<CancelMatchCommand, Result> handler,
            CancellationToken ct) =>
        {
            var callerId = user.GetUserId()!.Value;
            var command = new CancelMatchCommand(
                id,
                teamId,
                request.Reason,
                callerId);

            var result = await handler.HandleAsync(command, ct);
            return ToResult(result);
        })
        .WithName("CancelMatch")
        .WithSummary("Cancel a scheduled match before kick-off (Captain only)");

        // 6. Link match to pitch booking
        matches.MapPost("/{id:guid}/link-booking", async (
            Guid id,
            [FromQuery] Guid teamId,
            ClaimsPrincipal user,
            [FromBody] LinkMatchBookingRequest request,
            ICommandHandler<LinkMatchBookingCommand, Result<MatchDto>> handler,
            CancellationToken ct) =>
        {
            var callerId = user.GetUserId()!.Value;
            var command = new LinkMatchBookingCommand(
                id,
                teamId,
                request.BookingId,
                callerId);

            var result = await handler.HandleAsync(command, ct);
            return ToResult(result);
        })
        .WithName("LinkMatchBooking")
        .WithSummary("Link an existing pitch booking to a scheduled match (Captain only)");

        // 7. Submit match score
        matches.MapPost("/{id:guid}/results", async (
            Guid id,
            [FromQuery] Guid teamId,
            ClaimsPrincipal user,
            [FromBody] SubmitMatchResultRequest request,
            ICommandHandler<SubmitMatchResultCommand, Result<MatchDto>> handler,
            CancellationToken ct) =>
        {
            var callerId = user.GetUserId()!.Value;
            var command = new SubmitMatchResultCommand(
                id,
                teamId,
                request.HomeScore,
                request.AwayScore,
                callerId);

            var result = await handler.HandleAsync(command, ct);
            return ToResult(result);
        })
        .WithName("SubmitMatchResult")
        .WithSummary("Submit match scores (Captain of either team)");

        // 8. Verify or dispute match score
        matches.MapPost("/{id:guid}/results/verify", async (
            Guid id,
            [FromQuery] Guid teamId,
            ClaimsPrincipal user,
            [FromBody] VerifyMatchResultRequest request,
            ICommandHandler<VerifyMatchResultCommand, Result<MatchDto>> handler,
            CancellationToken ct) =>
        {
            var callerId = user.GetUserId()!.Value;
            var command = new VerifyMatchResultCommand(
                id,
                teamId,
                request.Action,
                request.DisputeReason,
                callerId);

            var result = await handler.HandleAsync(command, ct);
            return ToResult(result);
        })
        .WithName("VerifyMatchResult")
        .WithSummary("Confirm or dispute submitted match results (Opposing Captain only)");

        // 9. Admin dispute moderation queue
        matches.MapGet("/disputes", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            IQueryHandler<GetDisputedMatchesQuery, Result<PagedList<DisputedMatchDto>>> handler,
            CancellationToken ct) =>
        {
            var query = new GetDisputedMatchesQuery(page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize);
            var result = await handler.HandleAsync(query, ct);
            return ToResult(result);
        })
        .RequireAuthorization(Permissions.AdminDisputesResolve)
        .WithName("GetDisputedMatchesQueue")
        .WithSummary("Get disputed matches queue for moderation (Admin only)");
    }

    private static IResult ToResult(Result result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok();
        }

        return MapError(result.Error);
    }

    private static IResult ToResult<T>(Result<T> result, int successStatusCode = StatusCodes.Status200OK, string? location = null)
    {
        if (result.IsSuccess)
        {
            return successStatusCode switch
            {
                StatusCodes.Status201Created => Results.Created(location ?? string.Empty, result.Value),
                _ => Results.Ok(result.Value)
            };
        }

        return MapError(result.Error);
    }

    private static IResult MapError(Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Problem(
            title: error.Description,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}
