using Malabak.Modules.Matches.Application.Challenges.GetChallengeById;
using Malabak.Modules.Matches.Application.Challenges.GetTeamChallenges;
using Malabak.Modules.Matches.Application.Challenges.LinkBooking;
using Malabak.Modules.Matches.Application.Challenges.RespondToChallenge;
using Malabak.Modules.Matches.Application.Challenges.SendChallenge;
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
        services.AddScoped<ICommandHandler<SubmitMatchResultCommand, Result<MatchDto>>, SubmitMatchResultHandler>();
        services.AddScoped<ICommandHandler<VerifyMatchResultCommand, Result<MatchDto>>, VerifyMatchResultHandler>();
        services.AddScoped<ICommandHandler<Application.Matches.ResolveDispute.ResolveMatchDisputeCommand, Result<MatchDto>>, Application.Matches.ResolveDispute.ResolveMatchDisputeHandler>();

        // Domain Event Handlers
        services.AddScoped<Malabak.SharedKernel.Events.IDomainEventHandler<Malabak.Modules.Matches.Domain.Events.MatchResultConfirmedDomainEvent>, Malabak.Modules.Matches.Application.Matches.Events.MatchResultConfirmedDomainEventHandler>();

        // Background / Hangfire Jobs
        services.AddScoped<Malabak.Modules.Matches.Infrastructure.Jobs.RankingRecalculationJob>();
        services.AddScoped<Malabak.Modules.Matches.Infrastructure.Jobs.MatchResultTimeoutJob>();

        // Queries - Challenges
        services.AddScoped<IQueryHandler<GetChallengeByIdQuery, Result<TeamChallengeDto>>, GetChallengeByIdHandler>();
        services.AddScoped<IQueryHandler<GetTeamChallengesQuery, Result<IReadOnlyList<TeamChallengeDto>>>, GetTeamChallengesHandler>();

        // Queries - Matches
        services.AddScoped<IQueryHandler<GetMatchByIdQuery, Result<MatchDto>>, GetMatchByIdHandler>();
        services.AddScoped<IQueryHandler<GetUpcomingMatchesQuery, Result<PagedList<MatchDto>>>, GetUpcomingMatchesHandler>();
        services.AddScoped<IQueryHandler<GetMatchHistoryQuery, Result<PagedList<MatchDto>>>, GetMatchHistoryHandler>();
        services.AddScoped<IQueryHandler<GetDisputedMatchesQuery, Result<PagedList<DisputedMatchDto>>>, GetDisputedMatchesHandler>();
        services.AddScoped<IQueryHandler<Application.Matches.GetMatchAuditHistory.GetMatchAuditHistoryQuery, Result<IReadOnlyList<MatchResultAuditDto>>>, Application.Matches.GetMatchAuditHistory.GetMatchAuditHistoryHandler>();

        return services;
    }
}
