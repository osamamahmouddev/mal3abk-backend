using Hangfire;
using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Matches.Infrastructure.Jobs;

public sealed class RankingRecalculationJob
{
    public const string RecurringJobId = "team-ranking-recalculation-job";
    public const string CronSchedule = "0 1 * * *"; // Daily at 01:00 UTC (03:00 Cairo time)

    private readonly IMatchesDbContext _dbContext;
    private readonly ITeamRankingRecalculator _recalculator;
    private readonly IClock _clock;
    private readonly ILogger<RankingRecalculationJob> _logger;

    public RankingRecalculationJob(
        IMatchesDbContext dbContext,
        ITeamRankingRecalculator recalculator,
        IClock clock,
        ILogger<RankingRecalculationJob> logger)
    {
        _dbContext = dbContext;
        _recalculator = recalculator;
        _clock = clock;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task<int> ExecuteRecalculationAsync(CancellationToken cancellationToken = default)
    {
        var startTime = _clock.UtcNow;
        _logger.LogInformation("Starting nightly ranking recalculation sweep at {StartTime}", startTime);

        var confirmedMatches = await _dbContext.Matches
            .Where(m => m.Status == MatchStatus.Completed &&
                        m.ResultStatus == ResultVerificationStatus.Confirmed &&
                        m.HomeScore.HasValue &&
                        m.AwayScore.HasValue &&
                        !m.IsDeleted)
            .OrderBy(m => m.ResultVerifiedAtUtc ?? m.ScheduledStartUtc)
            .Select(m => new ConfirmedMatchRecord(
                m.Id,
                m.HomeTeamId,
                m.AwayTeamId,
                m.HomeScore!.Value,
                m.AwayScore!.Value,
                m.ResultVerifiedAtUtc ?? m.ScheduledStartUtc))
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Loaded {MatchCount} confirmed matches for chronological recalculation",
            confirmedMatches.Count);

        var result = await _recalculator.RecalculateAllAsync(confirmedMatches, cancellationToken);
        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Ranking recalculation failed: {Error}",
                result.Error.Description);
            throw new InvalidOperationException($"Ranking recalculation failed: {result.Error.Description}");
        }

        var duration = _clock.UtcNow - startTime;
        _logger.LogInformation(
            "Completed nightly ranking recalculation successfully in {DurationMs} ms for {MatchCount} matches",
            duration.TotalMilliseconds,
            confirmedMatches.Count);

        return confirmedMatches.Count;
    }
}
