using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Matches.Application.Admin;
using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Teams.Application.Admin;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>
/// Team and community moderation (TASK-705): team search and suspension, and correcting confirmed match results (rankings are
/// replayed). The report, appeal and disputed-result queues already live under <c>/teams/reports</c> and <c>/matches/disputes</c>.
/// </summary>
public static class AdminModerationEndpoints
{
    public static void MapAdminModerationEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var moderation = app.MapGroup("/api/v{version:apiVersion}/admin/moderation")
            .WithApiVersionSet(apiVersionSet).MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.AdminDisputesResolve)
            .AddEndpointFilter<AdminIpAllowlistFilter>()
            .WithTags("Admin Moderation");

        moderation.MapGet("/teams", async ([FromQuery] string? q, [FromQuery] bool? suspended, [FromQuery] int? page, [FromQuery] int? pageSize,
                HttpContext http, AdminTeamModerationService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.SearchAsync(q, suspended, page ?? 1, pageSize ?? 25, ct));
            })
            .Produces<AdminTeamPage>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminSearchTeams").WithSummary("Teams with members, rating and open fair-play reports against them");

        moderation.MapPost("/teams/{teamId:guid}/suspend", async (Guid teamId, [FromBody] AdminReasonApiRequest request, ClaimsPrincipal user, AdminTeamModerationService service, CancellationToken ct) =>
                AdminResults.From(await service.SuspendAsync(teamId, request.Reason, user.GetUserId()!.Value, ct)))
            .Produces<AdminTeamListItem>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminSuspendTeam").WithSummary("Suspend a team: no challenges, results, bookings by its captains or new members");

        moderation.MapPost("/teams/{teamId:guid}/reactivate", async (Guid teamId, [FromBody] AdminReasonApiRequest request, ClaimsPrincipal user, AdminTeamModerationService service, CancellationToken ct) =>
                AdminResults.From(await service.ReactivateAsync(teamId, request.Reason, user.GetUserId()!.Value, ct)))
            .Produces<AdminTeamListItem>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminReactivateTeam").WithSummary("Lift a team suspension");

        moderation.MapPost("/matches/{matchId:guid}/correct-result", async (Guid matchId, [FromBody] CorrectMatchResultApiRequest request, ClaimsPrincipal user,
                AdminMatchModerationService service, CancellationToken ct) =>
                AdminResults.From(await service.CorrectResultAsync(matchId, request.HomeScore, request.AwayScore, request.Reason, user.GetUserId()!.Value, ct)))
            .RequireStepUp(Malabak.Modules.Identity.Application.Common.StepUpScopes.AdminDestructive)
            .Produces<MatchDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminCorrectMatchResult").WithSummary("Correct a confirmed result (reason + step-up); team rankings are recalculated");
    }
}

public sealed record CorrectMatchResultApiRequest(int HomeScore, int AwayScore, string? Reason);
