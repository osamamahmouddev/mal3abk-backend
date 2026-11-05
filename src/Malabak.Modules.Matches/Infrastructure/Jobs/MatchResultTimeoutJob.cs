using Hangfire;
using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Matches.Infrastructure.Jobs;

public sealed class MatchResultTimeoutJob
{
    public const string RecurringJobId = "match-result-timeout-job";
    public const string CronSchedule = "*/15 * * * *"; // Every 15 minutes
    public const int DefaultVerificationTimeoutHours = 24;

    private readonly IMatchesDbContext _dbContext;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly IClock _clock;
    private readonly ILogger<MatchResultTimeoutJob> _logger;

    public MatchResultTimeoutJob(
        IMatchesDbContext dbContext,
        IDomainEventDispatcher domainEventDispatcher,
        IClock clock,
        ILogger<MatchResultTimeoutJob> logger)
    {
        _dbContext = dbContext;
        _domainEventDispatcher = domainEventDispatcher;
        _clock = clock;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public async Task<int> ExecuteSweepAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var cutoffUtc = nowUtc.AddHours(-DefaultVerificationTimeoutHours);

        var pendingMatches = await _dbContext.Matches
            .Where(m => m.ResultStatus == ResultVerificationStatus.PendingVerification &&
                        m.ResultSubmittedAtUtc.HasValue &&
                        m.ResultSubmittedAtUtc.Value <= cutoffUtc &&
                        !m.IsDeleted)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (pendingMatches.Count == 0)
        {
            return 0;
        }

        var autoConfirmedCount = 0;
        foreach (var match in pendingMatches)
        {
            var result = match.AutoConfirmResult(nowUtc);
            if (!result.IsSuccess)
            {
                _logger.LogWarning("Failed to auto-confirm match {MatchId}: {Error}", match.Id, result.Error.Description);
                continue;
            }

            autoConfirmedCount++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Dispatch domain events (triggers ranking updates via MatchResultConfirmedDomainEventHandler)
        var events = pendingMatches.SelectMany(m => m.DomainEvents).ToList();
        if (events.Count > 0)
        {
            await _domainEventDispatcher.DispatchAsync(events, cancellationToken);
        }

        _logger.LogInformation(
            "Auto-confirmed {Count} match results that exceeded the {Hours}h verification timeout",
            autoConfirmedCount,
            DefaultVerificationTimeoutHours);

        return autoConfirmedCount;
    }
}
