using Malabak.Modules.Matches.Application.Challenges.GetChallengeById;
using Malabak.Modules.Matches.Application.Challenges.GetTeamChallenges;
using Malabak.Modules.Matches.Application.Challenges.LinkBooking;
using Malabak.Modules.Matches.Application.Challenges.RespondToChallenge;
using Malabak.Modules.Matches.Application.Challenges.SendChallenge;
using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Application.Matches.CancelMatch;
using Malabak.Modules.Matches.Application.Matches.GetMatchById;
using Malabak.Modules.Matches.Application.Matches.GetMatchHistory;
using Malabak.Modules.Matches.Application.Matches.GetUpcomingMatches;
using Malabak.Modules.Matches.Application.Matches.LinkBooking;
using Malabak.Modules.Matches.Application.Matches.ScheduleMatch;
using Malabak.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Modules.Matches;

public static class MatchesModule
{
    public const string Schema = "matches";

    public static IServiceCollection AddMatchesModule(this IServiceCollection services)
    {
        // Commands - Challenges
        services.AddScoped<ICommandHandler<SendChallengeCommand, Result<TeamChallengeDto>>, SendChallengeHandler>();
        services.AddScoped<ICommandHandler<RespondToChallengeCommand, Result<TeamChallengeDto>>, RespondToChallengeHandler>();
        services.AddScoped<ICommandHandler<LinkChallengeBookingCommand, Result<TeamChallengeDto>>, LinkChallengeBookingHandler>();

        // Commands - Matches
        services.AddScoped<ICommandHandler<ScheduleMatchCommand, Result<MatchDto>>, ScheduleMatchHandler>();
        services.AddScoped<ICommandHandler<CancelMatchCommand, Result>, CancelMatchHandler>();
        services.AddScoped<ICommandHandler<LinkMatchBookingCommand, Result<MatchDto>>, LinkMatchBookingHandler>();

        // Queries - Challenges
        services.AddScoped<IQueryHandler<GetChallengeByIdQuery, Result<TeamChallengeDto>>, GetChallengeByIdHandler>();
        services.AddScoped<IQueryHandler<GetTeamChallengesQuery, Result<IReadOnlyList<TeamChallengeDto>>>, GetTeamChallengesHandler>();

        // Queries - Matches
        services.AddScoped<IQueryHandler<GetMatchByIdQuery, Result<MatchDto>>, GetMatchByIdHandler>();
        services.AddScoped<IQueryHandler<GetUpcomingMatchesQuery, Result<PagedList<MatchDto>>>, GetUpcomingMatchesHandler>();
        services.AddScoped<IQueryHandler<GetMatchHistoryQuery, Result<PagedList<MatchDto>>>, GetMatchHistoryHandler>();

        return services;
    }
}
