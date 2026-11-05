using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Application.Teams.CreateTeam;
using Malabak.Modules.Teams.Application.Teams.Invitations.AcceptTeamInvitation;
using Malabak.Modules.Teams.Application.Teams.Invitations.CreateTeamInvitation;
using Malabak.Modules.Teams.Application.Teams.Members.RemoveTeamMember;
using Malabak.Modules.Teams.Application.Teams.Members.TransferCaptaincy;
using Malabak.Modules.Teams.Application.Teams.Queries.DiscoverTeams;
using Malabak.Modules.Teams.Application.Teams.Queries.GetMyTeams;
using Malabak.Modules.Teams.Application.Teams.Queries.GetTeamById;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class TeamEndpoints
{
    public static void MapTeamEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var teams = app.MapGroup("/api/v{version:apiVersion}/teams")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.TeamsParticipate)
            .WithTags("Teams");

        // 0. Discover public teams
        teams.MapGet("/", async (
            ClaimsPrincipal user,
            [FromQuery] string? search,
            [FromQuery] string? city,
            [FromQuery] string? governorate,
            [FromQuery] string? district,
            [FromQuery] double? minRating,
            [FromQuery] double? maxRating,
            [FromQuery] bool? isRecruiting,
            [FromQuery] bool? isOpenForChallenges,
            [FromQuery] string? sortBy,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            IQueryHandler<DiscoverTeamsQuery, Result<PagedList<TeamDiscoveryDto>>> handler,
            CancellationToken ct) =>
        {
            var requestingPlayerId = user.GetUserId();
            var query = new DiscoverTeamsQuery(
                search,
                city,
                governorate,
                district,
                minRating,
                maxRating,
                isRecruiting,
                isOpenForChallenges,
                sortBy,
                page ?? 1,
                pageSize ?? 20,
                requestingPlayerId);

            var result = await handler.HandleAsync(query, ct);
            return ToResult(result);
        })
        .WithName("DiscoverTeams")
        .WithSummary("Discover public teams with filters, pagination, and challenge eligibility");

        // 1. Create team (Creator becomes Captain)
        teams.MapPost("/", async (
            ClaimsPrincipal user,
            [FromBody] CreateTeamApiRequest request,
            ICommandHandler<CreateTeamCommand, Result<TeamDetailDto>> handler,
            CancellationToken ct) =>
        {
            var playerId = user.GetUserId()!.Value;
            var command = new CreateTeamCommand(
                playerId,
                request.Name,
                request.Description,
                request.City,
                request.Governorate,
                request.District,
                request.LogoUrl,
                request.CoverUrl);

            var result = await handler.HandleAsync(command, ct);
            return ToResult(result, StatusCodes.Status201Created, result.IsSuccess ? $"/api/v1/teams/{result.Value.Id}" : null);
        })
        .WithName("CreateTeam")
        .WithSummary("Create a new football team (authenticated player becomes Captain)");

        // 2. Generate or rotate shareable 6-character invite code
        teams.MapPost("/{id:guid}/invitations", async (
            Guid id,
            ClaimsPrincipal user,
            [FromBody] CreateInvitationApiRequest? request,
            ICommandHandler<CreateTeamInvitationCommand, Result<TeamInvitationDto>> handler,
            CancellationToken ct) =>
        {
            var playerId = user.GetUserId()!.Value;
            var command = new CreateTeamInvitationCommand(id, playerId, request?.ValidityDays ?? 7);
            var result = await handler.HandleAsync(command, ct);
            return ToResult(result);
        })
        .WithName("CreateTeamInvitation")
        .WithSummary("Generate or rotate a unique 6-character team invite code (Captain or ViceCaptain only)");

        // 3. Accept invitation via 6-character code
        teams.MapPost("/invitations/accept", async (
            ClaimsPrincipal user,
            [FromBody] AcceptInvitationApiRequest request,
            ICommandHandler<AcceptTeamInvitationCommand, Result<TeamDetailDto>> handler,
            CancellationToken ct) =>
        {
            var playerId = user.GetUserId()!.Value;
            var command = new AcceptTeamInvitationCommand(request.InviteCode, playerId);
            var result = await handler.HandleAsync(command, ct);
            return ToResult(result);
        })
        .WithName("AcceptTeamInvitation")
        .WithSummary("Join a team using a unique 6-character invite code");

        // 4. Remove member or leave team
        teams.MapDelete("/{id:guid}/members/{memberPlayerId:guid}", async (
            Guid id,
            Guid memberPlayerId,
            ClaimsPrincipal user,
            ICommandHandler<RemoveTeamMemberCommand, Result> handler,
            CancellationToken ct) =>
        {
            var requestingPlayerId = user.GetUserId()!.Value;
            var command = new RemoveTeamMemberCommand(id, memberPlayerId, requestingPlayerId);
            var result = await handler.HandleAsync(command, ct);
            return ToResult(result, StatusCodes.Status204NoContent);
        })
        .WithName("RemoveTeamMember")
        .WithSummary("Remove a member from team roster (Captain) or leave team (Self)");

        // 5. Transfer captaincy
        teams.MapPost("/{id:guid}/transfer-captaincy", async (
            Guid id,
            ClaimsPrincipal user,
            [FromBody] TransferCaptaincyApiRequest request,
            ICommandHandler<TransferCaptaincyCommand, Result> handler,
            CancellationToken ct) =>
        {
            var currentCaptainId = user.GetUserId()!.Value;
            var command = new TransferCaptaincyCommand(id, request.NewCaptainPlayerId, currentCaptainId);
            var result = await handler.HandleAsync(command, ct);
            return ToResult(result);
        })
        .WithName("TransferTeamCaptaincy")
        .WithSummary("Transfer team captaincy to an active team member (Captain only)");

        // 6. Get team by ID
        teams.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            IQueryHandler<GetTeamByIdQuery, Result<TeamDetailDto>> handler,
            CancellationToken ct) =>
        {
            var requestingPlayerId = user.GetUserId();
            var result = await handler.HandleAsync(new GetTeamByIdQuery(id, requestingPlayerId), ct);
            return ToResult(result);
        })
        .WithName("GetTeamById")
        .WithSummary("Get team details and roster");

        // 7. My teams
        var me = app.MapGroup("/api/v{version:apiVersion}/me")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.TeamsParticipate)
            .WithTags("Teams");

        me.MapGet("/teams", async (
            ClaimsPrincipal user,
            IQueryHandler<GetMyTeamsQuery, Result<IReadOnlyList<TeamSummaryDto>>> handler,
            CancellationToken ct) =>
        {
            var playerId = user.GetUserId()!.Value;
            var result = await handler.HandleAsync(new GetMyTeamsQuery(playerId), ct);
            return ToResult(result);
        })
        .WithName("GetMyTeams")
        .WithSummary("List all teams where authenticated player is an active member");
    }

    private static IResult ToResult(Result result, int successStatus = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
        {
            return successStatus == StatusCodes.Status204NoContent
                ? Results.NoContent()
                : Results.Ok();
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

    public sealed record CreateTeamApiRequest(
        string Name,
        string? Description,
        string City,
        string Governorate,
        string? District,
        string? LogoUrl,
        string? CoverUrl);

    public sealed record CreateInvitationApiRequest(int? ValidityDays);

    public sealed record AcceptInvitationApiRequest(string InviteCode);

    public sealed record TransferCaptaincyApiRequest(Guid NewCaptainPlayerId);
}
