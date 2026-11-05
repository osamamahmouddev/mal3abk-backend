using Malabak.Modules.Matches.Application.Challenges.GetChallengeById;
using Malabak.Modules.Matches.Application.Challenges.GetTeamChallenges;
using Malabak.Modules.Matches.Application.Challenges.RespondToChallenge;
using Malabak.Modules.Matches.Application.Challenges.SendChallenge;
using Malabak.Modules.Matches.Application.Common;
using Malabak.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Modules.Matches;

public static class MatchesModule
{
    public const string Schema = "matches";

    public static IServiceCollection AddMatchesModule(this IServiceCollection services)
    {
        // Commands
        services.AddScoped<ICommandHandler<SendChallengeCommand, Result<TeamChallengeDto>>, SendChallengeHandler>();
        services.AddScoped<ICommandHandler<RespondToChallengeCommand, Result<TeamChallengeDto>>, RespondToChallengeHandler>();

        // Queries
        services.AddScoped<IQueryHandler<GetChallengeByIdQuery, Result<TeamChallengeDto>>, GetChallengeByIdHandler>();
        services.AddScoped<IQueryHandler<GetTeamChallengesQuery, Result<IReadOnlyList<TeamChallengeDto>>>, GetTeamChallengesHandler>();

        return services;
    }
}
