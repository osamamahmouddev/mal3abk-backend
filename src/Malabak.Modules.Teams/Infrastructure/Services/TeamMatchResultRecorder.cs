using Malabak.Modules.Teams.Application.Common;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Ranking;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Infrastructure.Services;

public sealed class TeamMatchResultRecorder : ITeamMatchResultRecorder
{
    private readonly ITeamsDbContext _dbContext;
    private readonly IRankingStrategy _rankingStrategy;
    private readonly IClock _clock;

    public TeamMatchResultRecorder(
        ITeamsDbContext dbContext,
        IRankingStrategy rankingStrategy,
        IClock clock)
    {
        _dbContext = dbContext;
        _rankingStrategy = rankingStrategy;
        _clock = clock;
    }

    public async Task RecordConfirmedResultAsync(
        Guid homeTeamId,
        Guid awayTeamId,
        int homeScore,
        int awayScore,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var homeTeam = await _dbContext.Teams
            .FirstOrDefaultAsync(t => t.Id == homeTeamId, cancellationToken);

        var awayTeam = await _dbContext.Teams
            .FirstOrDefaultAsync(t => t.Id == awayTeamId, cancellationToken);

        if (homeTeam is not null && awayTeam is not null)
        {
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

            var calculation = _rankingStrategy.CalculateMatch(homeSnapshot, awaySnapshot, homeScore, awayScore);

            homeTeam.UpdateRanking(
                calculation.Home.NewRating,
                calculation.Home.NewRatingDeviation,
                calculation.Home.NewVolatility,
                nowUtc);

            awayTeam.UpdateRanking(
                calculation.Away.NewRating,
                calculation.Away.NewRatingDeviation,
                calculation.Away.NewVolatility,
                nowUtc);

            homeTeam.RecordMatchResult(
                calculation.Home.IsWin,
                calculation.Home.IsDraw,
                calculation.Home.IsLoss,
                homeScore,
                awayScore,
                nowUtc);

            awayTeam.RecordMatchResult(
                calculation.Away.IsWin,
                calculation.Away.IsDraw,
                calculation.Away.IsLoss,
                awayScore,
                homeScore,
                nowUtc);
        }
        else
        {
            if (homeTeam is not null)
            {
                var isWin = homeScore > awayScore;
                var isDraw = homeScore == awayScore;
                var isLoss = homeScore < awayScore;
                homeTeam.RecordMatchResult(isWin, isDraw, isLoss, homeScore, awayScore, nowUtc);
            }

            if (awayTeam is not null)
            {
                var isWin = awayScore > homeScore;
                var isDraw = awayScore == homeScore;
                var isLoss = awayScore < homeScore;
                awayTeam.RecordMatchResult(isWin, isDraw, isLoss, awayScore, homeScore, nowUtc);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
