using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Application.Teams.CreateTeam;
using Malabak.Modules.Teams.Application.Teams.Invitations.AcceptTeamInvitation;
using Malabak.Modules.Teams.Application.Teams.Invitations.CreateTeamInvitation;
using Malabak.Modules.Teams.Application.Teams.Members.RemoveTeamMember;
using Malabak.Modules.Teams.Application.Teams.Members.TransferCaptaincy;
using Malabak.Modules.Teams.Application.Teams.Queries.DiscoverTeams;
using Malabak.Modules.Teams.Application.Teams.Queries.GetMyTeams;
using Malabak.Modules.Teams.Application.Teams.Queries.GetTeamById;
using Malabak.Modules.Teams.Application.Teams.Queries.GetTeamProfile;
using Malabak.Modules.Teams.Application.Teams.Queries.GetTeamStats;
using Malabak.Modules.Teams.Domain;
using Malabak.Modules.Teams.Infrastructure.Services;
using Malabak.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Modules.Teams;

public static class TeamsModule
{
    public const string Schema = "teams";

    public static IServiceCollection AddTeamsModule(this IServiceCollection services)
    {
        // Domain services / generators
        services.AddScoped<ITeamSlugGenerator, TeamSlugGenerator>();
        services.AddScoped<ITeamInviteCodeGenerator, TeamInviteCodeGenerator>();

        // Commands
        services.AddScoped<ICommandHandler<CreateTeamCommand, Result<TeamDetailDto>>, CreateTeamHandler>();
        services.AddScoped<ICommandHandler<CreateTeamInvitationCommand, Result<TeamInvitationDto>>, CreateTeamInvitationHandler>();
        services.AddScoped<ICommandHandler<AcceptTeamInvitationCommand, Result<TeamDetailDto>>, AcceptTeamInvitationHandler>();
        services.AddScoped<ICommandHandler<RemoveTeamMemberCommand, Result>, RemoveTeamMemberHandler>();
        services.AddScoped<ICommandHandler<TransferCaptaincyCommand, Result>, TransferCaptaincyHandler>();

        // Queries
        services.AddScoped<IQueryHandler<GetTeamByIdQuery, Result<TeamDetailDto>>, GetTeamByIdHandler>();
        services.AddScoped<IQueryHandler<GetMyTeamsQuery, Result<IReadOnlyList<TeamSummaryDto>>>, GetMyTeamsHandler>();
        services.AddScoped<IQueryHandler<DiscoverTeamsQuery, Result<PagedList<TeamDiscoveryDto>>>, DiscoverTeamsHandler>();
        services.AddScoped<IQueryHandler<GetTeamProfileQuery, Result<TeamProfileDto>>, GetTeamProfileHandler>();
        services.AddScoped<IQueryHandler<GetTeamStatsQuery, Result<TeamStatsDto>>, GetTeamStatsHandler>();

        return services;
    }
}
