using Malabak.Api.Infrastructure.Audit;
using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Application.Teams.CreateTeam;
using Malabak.Modules.Teams.Application.Teams.Invitations.AcceptTeamInvitation;
using Malabak.Modules.Teams.Application.Teams.Invitations.CreateTeamInvitation;
using Malabak.Modules.Teams.Application.Teams.JoinRequests.ApproveJoinRequest;
using Malabak.Modules.Teams.Application.Teams.JoinRequests.DeclineJoinRequest;
using Malabak.Modules.Teams.Application.Teams.JoinRequests.GetTeamJoinRequests;
using Malabak.Modules.Teams.Application.Teams.JoinRequests.RequestToJoinTeam;
using Malabak.Modules.Teams.Application.Teams.Members.LeaveTeam;
using Malabak.Modules.Teams.Application.Teams.Members.RemoveTeamMember;
using Malabak.Modules.Teams.Application.Teams.Members.TransferCaptaincy;
using Malabak.Modules.Teams.Application.Teams.Queries.DiscoverTeams;
using Malabak.Modules.Teams.Application.Teams.Queries.GetMyTeams;
using Malabak.Modules.Teams.Application.Teams.Queries.GetTeamById;
using Malabak.Modules.Teams.Application.Teams.Queries.GetTeamProfile;
using Malabak.Modules.Teams.Application.Teams.Queries.GetTeamStats;
using Malabak.Modules.Teams.Application.Teams.Settings.UpdateTeamSettings;
using Malabak.Modules.Teams.Domain;
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
            var command = new CreateTeamInvitationCommand(id, playerId, request?.ValidityDays ?? 7, request?.InvitedPlayerId);
            var result = await handler.HandleAsync(command, ct);
            return ToResult(result);
        })
        .WithName("CreateTeamInvitation")
        .WithSummary("Generate or rotate a unique 6-character team invite code (Captain or ViceCaptain only)");

        // 3. Accept invitation via 6-character code
        teams.MapPost("/invitations/accept", async (
            ClaimsPrincipal user,
            [FromBody] AcceptTeamInvitationApiRequest request,
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

        // 6b. Get team profile (header, roster, stats, challenge eligibility, upcoming/recent matches)
        teams.MapGet("/{id:guid}/profile", async (
            Guid id,
            ClaimsPrincipal user,
            IQueryHandler<GetTeamProfileQuery, Result<TeamProfileDto>> handler,
            CancellationToken ct) =>
        {
            var requestingPlayerId = user.GetUserId();
            var result = await handler.HandleAsync(new GetTeamProfileQuery(id, requestingPlayerId), ct);
            return ToResult(result);
        })
        .WithName("GetTeamProfile")
        .WithSummary("Get full team profile including stats projection and roster");

        // 6c. Get team statistics projection
        teams.MapGet("/{id:guid}/stats", async (
            Guid id,
            IQueryHandler<GetTeamStatsQuery, Result<TeamStatsDto>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetTeamStatsQuery(id), ct);
            return ToResult(result);
        })
        .WithName("GetTeamStats")
        .WithSummary("Get projected team statistics (W/D/L, goals, win %, ranking)");

        // 6d. Request to join team
        teams.MapPost("/{id:guid}/join-requests", async (
            Guid id,
            ClaimsPrincipal user,
            [FromBody] RequestToJoinTeamApiRequest? request,
            ICommandHandler<RequestToJoinTeamCommand, Result<TeamJoinRequestDto>> handler,
            CancellationToken ct) =>
        {
            var playerId = user.GetUserId()!.Value;
            var command = new RequestToJoinTeamCommand(id, playerId, request?.Message);
            var result = await handler.HandleAsync(command, ct);
            return ToResult(result, StatusCodes.Status201Created);
        })
        .WithName("RequestToJoinTeam")
        .WithSummary("Submit a request to join a recruiting team");

        // 6e. List team join requests (Captain or ViceCaptain only)
        teams.MapGet("/{id:guid}/join-requests", async (
            Guid id,
            ClaimsPrincipal user,
            [FromQuery] JoinRequestStatus? status,
            IQueryHandler<GetTeamJoinRequestsQuery, Result<IReadOnlyList<TeamJoinRequestDto>>> handler,
            CancellationToken ct) =>
        {
            var playerId = user.GetUserId()!.Value;
            var query = new GetTeamJoinRequestsQuery(id, playerId, status);
            var result = await handler.HandleAsync(query, ct);
            return ToResult(result);
        })
        .WithName("GetTeamJoinRequests")
        .WithSummary("List join requests for team (Captain or ViceCaptain only)");

        // 6f. Approve join request
        teams.MapPost("/{id:guid}/join-requests/{requestId:guid}/approve", async (
            Guid id,
            Guid requestId,
            ClaimsPrincipal user,
            ICommandHandler<ApproveJoinRequestCommand, Result> handler,
            CancellationToken ct) =>
        {
            var reviewerId = user.GetUserId()!.Value;
            var command = new ApproveJoinRequestCommand(id, requestId, reviewerId);
            var result = await handler.HandleAsync(command, ct);
            return ToResult(result);
        })
        .WithName("ApproveTeamJoinRequest")
        .WithSummary("Approve a join request (Captain or ViceCaptain only)");

        // 6g. Decline join request
        teams.MapPost("/{id:guid}/join-requests/{requestId:guid}/decline", async (
            Guid id,
            Guid requestId,
            ClaimsPrincipal user,
            ICommandHandler<DeclineJoinRequestCommand, Result> handler,
            CancellationToken ct) =>
        {
            var reviewerId = user.GetUserId()!.Value;
            var command = new DeclineJoinRequestCommand(id, requestId, reviewerId);
            var result = await handler.HandleAsync(command, ct);
            return ToResult(result);
        })
        .WithName("DeclineTeamJoinRequest")
        .WithSummary("Decline a join request (Captain or ViceCaptain only)");

        // 6h. Leave team
        teams.MapPost("/{id:guid}/leave", async (
            Guid id,
            ClaimsPrincipal user,
            ICommandHandler<LeaveTeamCommand, Result> handler,
            CancellationToken ct) =>
        {
            var playerId = user.GetUserId()!.Value;
            var command = new LeaveTeamCommand(id, playerId);
            var result = await handler.HandleAsync(command, ct);
            return ToResult(result, StatusCodes.Status204NoContent);
        })
        .WithName("LeaveTeam")
        .WithSummary("Leave team (members only; captain must transfer captaincy first)");

        // 6i. Update team settings (visibility, recruiting, challenges)
        teams.MapPut("/{id:guid}/settings", async (
            Guid id,
            ClaimsPrincipal user,
            [FromBody] UpdateTeamSettingsRequest request,
            ICommandHandler<UpdateTeamSettingsCommand, Result> handler,
            CancellationToken ct) =>
        {
            var callerId = user.GetUserId()!.Value;
            var command = new UpdateTeamSettingsCommand(
                id,
                callerId,
                request.IsPublic,
                request.IsRecruiting,
                request.IsOpenForChallenges);
            var result = await handler.HandleAsync(command, ct);
            return ToResult(result);
        })
        .WithName("UpdateTeamSettings")
        .WithSummary("Update team visibility and recruitment settings (Captain or ViceCaptain only)");

        // 6j. Team Leaderboard
        teams.MapGet("/leaderboard", async (
            [FromQuery] string? city,
            [FromQuery] string? governorate,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            IQueryHandler<Malabak.Modules.Teams.Application.Teams.Queries.GetTeamLeaderboard.GetTeamLeaderboardQuery, Result<PagedList<TeamLeaderboardEntryDto>>> handler,
            CancellationToken ct) =>
        {
            var query = new Malabak.Modules.Teams.Application.Teams.Queries.GetTeamLeaderboard.GetTeamLeaderboardQuery(city, governorate, page, pageSize);
            var result = await handler.HandleAsync(query, ct);
            return ToResult(result);
        })
        .AllowAnonymous()
        .WithName("GetTeamLeaderboard")
        .WithSummary("Get team competitive rankings leaderboard (overall or filtered by city/governorate)");

        // 6k. Submit fair-play report
        teams.MapPost("/{id:guid}/report", async (
            Guid id,
            [FromBody] SubmitFairPlayReportRequest request,
            ClaimsPrincipal user,
            ICommandHandler<Malabak.Modules.Teams.Application.Teams.FairPlay.SubmitReport.SubmitFairPlayReportCommand, Result<FairPlayReportDto>> handler,
            CancellationToken ct) =>
        {
            var callerId = user.GetUserId()!.Value;
            var command = new Malabak.Modules.Teams.Application.Teams.FairPlay.SubmitReport.SubmitFairPlayReportCommand(
                request.ReporterTeamId,
                callerId,
                id,
                request.Category,
                request.Description,
                request.MatchId,
                request.EvidenceUrls);

            var result = await handler.HandleAsync(command, ct);
            return ToResult(result, StatusCodes.Status201Created);
        })
        .WithName("SubmitFairPlayReport")
        .WithSummary("Submit a fair-play violation report against a team (Captain or ViceCaptain only)");

        // 6l. Admin get fair-play reports queue
        teams.MapGet("/reports", async (
            [FromQuery] FairPlayReportStatus? status,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            IQueryHandler<Malabak.Modules.Teams.Application.Teams.FairPlay.GetReports.GetFairPlayReportsQuery, Result<PagedList<FairPlayReportDto>>> handler,
            CancellationToken ct) =>
        {
            var query = new Malabak.Modules.Teams.Application.Teams.FairPlay.GetReports.GetFairPlayReportsQuery(status, page, pageSize);
            var result = await handler.HandleAsync(query, ct);
            return ToResult(result);
        })
        .RequireAuthorization(Permissions.AdminDisputesResolve)
        .Produces<PagedList<FairPlayReportDto>>()
        .WithName("GetFairPlayReportsQueue")
        .WithSummary("Get fair-play reports queue for admin moderation (Admin only)");

        // 6m. Admin review fair-play report
        teams.MapPost("/reports/{id:guid}/review", async (
            Guid id,
            [FromBody] ReviewFairPlayReportRequest request,
            ClaimsPrincipal user,
            ICommandHandler<Malabak.Modules.Teams.Application.Teams.FairPlay.ReviewReport.ReviewFairPlayReportCommand, Result<FairPlayReportDto>> handler,
            CancellationToken ct) =>
        {
            var adminId = user.GetUserId()!.Value;
            var command = new Malabak.Modules.Teams.Application.Teams.FairPlay.ReviewReport.ReviewFairPlayReportCommand(
                id,
                adminId,
                request.IsApproved,
                request.Penalty,
                request.PointsDeducted,
                request.AdminNotes);

            var result = await handler.HandleAsync(command, ct);
            return ToResult(result);
        })
        .RequireAuthorization(Permissions.AdminDisputesResolve)
        .Audited("admin.fair_play_report.reviewed", "TeamFairPlayReport")
        .Produces<FairPlayReportDto>()
        .WithName("ReviewFairPlayReport")
        .WithSummary("Admin review fair-play report (approve with penalty or dismiss) (Admin only)");

        // 6n. Team appeal fair-play report
        teams.MapPost("/reports/{id:guid}/appeal", async (
            Guid id,
            [FromBody] AppealFairPlayReportRequest request,
            ClaimsPrincipal user,
            ICommandHandler<Malabak.Modules.Teams.Application.Teams.FairPlay.AppealReport.AppealFairPlayReportCommand, Result<FairPlayReportDto>> handler,
            CancellationToken ct) =>
        {
            var captainId = user.GetUserId()!.Value;
            var command = new Malabak.Modules.Teams.Application.Teams.FairPlay.AppealReport.AppealFairPlayReportCommand(
                id,
                captainId,
                request.AppealReason);

            var result = await handler.HandleAsync(command, ct);
            return ToResult(result);
        })
        .WithName("AppealFairPlayReport")
        .WithSummary("Appeal an approved fair-play report penalty (Captain of reported team only)");

        // 6o. Admin resolve fair-play appeal
        teams.MapPost("/reports/{id:guid}/appeal/resolve", async (
            Guid id,
            [FromBody] ResolveFairPlayAppealRequest request,
            ClaimsPrincipal user,
            ICommandHandler<Malabak.Modules.Teams.Application.Teams.FairPlay.ResolveAppeal.ResolveFairPlayAppealCommand, Result<FairPlayReportDto>> handler,
            CancellationToken ct) =>
        {
            var adminId = user.GetUserId()!.Value;
            var command = new Malabak.Modules.Teams.Application.Teams.FairPlay.ResolveAppeal.ResolveFairPlayAppealCommand(
                id,
                adminId,
                request.UpholdViolation,
                request.AdminNotes);

            var result = await handler.HandleAsync(command, ct);
            return ToResult(result);
        })
        .RequireAuthorization(Permissions.AdminDisputesResolve)
        .Audited("admin.fair_play_appeal.resolved", "TeamFairPlayReport")
        .Produces<FairPlayReportDto>()
        .WithName("ResolveFairPlayAppeal")
        .WithSummary("Admin resolve a fair-play report appeal (uphold or overturn) (Admin only)");

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

    public sealed record CreateInvitationApiRequest(int? ValidityDays, Guid? InvitedPlayerId = null);

    public sealed record AcceptTeamInvitationApiRequest(string InviteCode);

    public sealed record TransferCaptaincyApiRequest(Guid NewCaptainPlayerId);

    public sealed record RequestToJoinTeamApiRequest(string? Message);
}
