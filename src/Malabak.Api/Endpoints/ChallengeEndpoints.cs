using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Matches.Application.Challenges.GetChallengeById;
using Malabak.Modules.Matches.Application.Challenges.GetTeamChallenges;
using Malabak.Modules.Matches.Application.Challenges.RespondToChallenge;
using Malabak.Modules.Matches.Application.Challenges.SendChallenge;
using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class ChallengeEndpoints
{
    public static void MapChallengeEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var matches = app.MapGroup("/api/v{version:apiVersion}/matches/challenges")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.TeamsParticipate)
            .WithTags("Matches", "Challenges");

        // 1. Send challenge to another team
        matches.MapPost("/", async (
            ClaimsPrincipal user,
            [FromBody] SendChallengeRequest request,
            ICommandHandler<SendChallengeCommand, Result<TeamChallengeDto>> handler,
            CancellationToken ct) =>
        {
            var callerId = user.GetUserId()!.Value;
            var command = new SendChallengeCommand(
                request.ChallengerTeamId,
                request.ChallengedTeamId,
                callerId,
                request.ProposedDateUtc,
                request.DurationMinutes,
                request.ProposedPitchId,
                request.ProposedVenueId,
                request.ProposedVenueName,
                request.Notes);

            var result = await handler.HandleAsync(command, ct);
            return ToResult(result, StatusCodes.Status201Created, result.IsSuccess ? $"/api/v1/matches/challenges/{result.Value.Id}" : null);
        })
        .WithName("SendChallenge")
        .WithSummary("Send a match challenge to another team (Captain or ViceCaptain only)");

        // 2. Respond to challenge (Accept, Decline, CounterPropose)
        matches.MapPost("/{id:guid}/respond", async (
            Guid id,
            ClaimsPrincipal user,
            [FromBody] RespondToChallengeRequest request,
            ICommandHandler<RespondToChallengeCommand, Result<TeamChallengeDto>> handler,
            CancellationToken ct) =>
        {
            var callerId = user.GetUserId()!.Value;
            var command = new RespondToChallengeCommand(
                id,
                request.RespondingTeamId,
                callerId,
                request.Action,
                request.NewProposedDateUtc,
                request.NewDurationMinutes,
                request.NewProposedPitchId,
                request.NewProposedVenueId,
                request.NewProposedVenueName,
                request.Notes,
                request.DeclineReason);

            var result = await handler.HandleAsync(command, ct);
            return ToResult(result);
        })
        .WithName("RespondToChallenge")
        .WithSummary("Respond to a challenge: Accept, Decline, or CounterPropose (Acceptance does not automatically book a pitch)");

        // 3. Get challenge by ID
        matches.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            IQueryHandler<GetChallengeByIdQuery, Result<TeamChallengeDto>> handler,
            CancellationToken ct) =>
        {
            var callerId = user.GetUserId()!.Value;
            var result = await handler.HandleAsync(new GetChallengeByIdQuery(id, callerId), ct);
            return ToResult(result);
        })
        .WithName("GetChallengeById")
        .WithSummary("Get match challenge details");

        // 4. List team challenges
        var teamChallenges = app.MapGroup("/api/v{version:apiVersion}/teams/{teamId:guid}/challenges")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.TeamsParticipate)
            .WithTags("Matches", "Challenges");

        teamChallenges.MapGet("/", async (
            Guid teamId,
            ClaimsPrincipal user,
            [FromQuery] ChallengeStatus? status,
            IQueryHandler<GetTeamChallengesQuery, Result<IReadOnlyList<TeamChallengeDto>>> handler,
            CancellationToken ct) =>
        {
            var callerId = user.GetUserId()!.Value;
            var result = await handler.HandleAsync(new GetTeamChallengesQuery(teamId, callerId, status), ct);
            return ToResult(result);
        })
        .WithName("GetTeamChallenges")
        .WithSummary("List challenges (sent and received) for a team (Captain or ViceCaptain only)");
    }

    private static IResult ToResult<T>(Result<T> result, int successStatus = StatusCodes.Status200OK, string? locationUri = null)
    {
        if (result.IsSuccess)
        {
            return successStatus == StatusCodes.Status201Created && !string.IsNullOrEmpty(locationUri)
                ? Results.Created(locationUri, result.Value)
                : Results.Ok(result.Value);
        }

        var status = result.Error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status400BadRequest
        };

        return Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: status);
    }
}
