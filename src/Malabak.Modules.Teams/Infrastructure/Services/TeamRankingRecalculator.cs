using Malabak.Modules.Teams.Application.Common;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Ranking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Teams.Infrastructure.Services;

public sealed class TeamRankingRecalculator : ITeamRankingRecalculator
{
    private readonly ITeamsDbContext _dbContext;
    private readonly IRankingStrategy _rankingStrategy;
    private readonly IClock _clock;
    private readonly ILogger<TeamRankingRecalculator> _logger;

    public TeamRankingRecalculator(
        ITeamsDbContext dbContext,
        IRankingStrategy rankingStrategy,
        IClock clock,
        ILogger<TeamRankingRecalculator> logger)
    {
        _dbContext = dbContext;
        _rankingStrategy = rankingStrategy;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> RecalculateAllAsync(
        IReadOnlyList<ConfirmedMatchRecord> confirmedMatches,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmedMatches);

        var nowUtc = _clock.UtcNow;
        var teams = await _dbContext.Teams
            .Where(t => !t.IsDeleted)
            .ToListAsync(cancellationToken);

        var teamsById = teams.ToDictionary(t => t.Id);
        var lastMatchDateByTeam = new Dictionary<Guid, DateTime>();

        // 1. Reset all teams to default baseline
        foreach (var team in teams)
        {
            team.ResetStats(nowUtc);
        }

        // 2. Sort confirmed matches chronologically
        var sortedMatches = confirmedMatches
            .OrderBy(m => m.CompletedAtUtc)
            .ToList();

        // 3. Replay matches sequentially
        foreach (var match in sortedMatches)
        {
            if (!teamsById.TryGetValue(match.HomeTeamId, out var homeTeam) ||
                !teamsById.TryGetValue(match.AwayTeamId, out var awayTeam))
            {
                continue;
            }

            // Inactivity decay between matches if > 30 days
            if (lastMatchDateByTeam.TryGetValue(homeTeam.Id, out var lastHomeDate))
            {
                var daysInactive = (int)(match.CompletedAtUtc - lastHomeDate).TotalDays;
                if (daysInactive > 30)
                {
                    var decayedRd = _rankingStrategy.CalculateInactivityDecay(homeTeam.RatingDeviation, homeTeam.RatingVolatility, daysInactive);
                    homeTeam.ApplyInactivityDecay(decayedRd, match.CompletedAtUtc);
                }
            }

            if (lastMatchDateByTeam.TryGetValue(awayTeam.Id, out var lastAwayDate))
            {
                var daysInactive = (int)(match.CompletedAtUtc - lastAwayDate).TotalDays;
                if (daysInactive > 30)
                {
                    var decayedRd = _rankingStrategy.CalculateInactivityDecay(awayTeam.RatingDeviation, awayTeam.RatingVolatility, daysInactive);
                    awayTeam.ApplyInactivityDecay(decayedRd, match.CompletedAtUtc);
                }
            }

            var homeSnapshot = new TeamRatingSnapshot(
                homeTeam.Id,
                homeTeam.Rating,
                homeTeam.RatingDeviation,
                homeTeam.RatingVolatility);

            var awaySnapshot = new TeamRatingSnapshot(
                awayTeam.Id,
                awayTeam.Rating,
                awayTeam.RatingDeviation,
                awayTeam.RatingVolatility);

            var outcome = _rankingStrategy.CalculateMatch(homeSnapshot, awaySnapshot, match.HomeScore, match.AwayScore);

            homeTeam.UpdateRanking(
                outcome.Home.NewRating,
                outcome.Home.NewRatingDeviation,
                outcome.Home.NewVolatility,
                match.CompletedAtUtc);

            awayTeam.UpdateRanking(
                outcome.Away.NewRating,
                outcome.Away.NewRatingDeviation,
                outcome.Away.NewVolatility,
                match.CompletedAtUtc);

            homeTeam.RecordMatchResult(
                outcome.Home.IsWin,
                outcome.Home.IsDraw,
                outcome.Home.IsLoss,
                match.HomeScore,
                match.AwayScore,
                match.CompletedAtUtc);

            awayTeam.RecordMatchResult(
                outcome.Away.IsWin,
                outcome.Away.IsDraw,
                outcome.Away.IsLoss,
                match.AwayScore,
                match.HomeScore,
                match.CompletedAtUtc);

            lastMatchDateByTeam[homeTeam.Id] = match.CompletedAtUtc;
            lastMatchDateByTeam[awayTeam.Id] = match.CompletedAtUtc;
        }

        // 4. Inactivity decay up to current time for teams inactive > 30 days
        foreach (var team in teams)
        {
            if (lastMatchDateByTeam.TryGetValue(team.Id, out var lastDate))
            {
                var daysInactive = (int)(nowUtc - lastDate).TotalDays;
                if (daysInactive > 30)
                {
                    var decayedRd = _rankingStrategy.CalculateInactivityDecay(team.RatingDeviation, team.RatingVolatility, daysInactive);
                    team.ApplyInactivityDecay(decayedRd, nowUtc);
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Ranking recalculation complete: replayed {MatchCount} matches across {TeamCount} teams using strategy {StrategyName}",
            sortedMatches.Count,
            teams.Count,
            _rankingStrategy.StrategyName);

        return Result.Success();
    }
}
