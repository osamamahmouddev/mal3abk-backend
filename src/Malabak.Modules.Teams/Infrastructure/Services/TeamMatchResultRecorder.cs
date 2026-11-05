using Malabak.Modules.Teams.Application.Common;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Infrastructure.Services;

public sealed class TeamMatchResultRecorder : ITeamMatchResultRecorder
{
    private readonly ITeamsDbContext _dbContext;
    private readonly IClock _clock;

    public TeamMatchResultRecorder(ITeamsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
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

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
