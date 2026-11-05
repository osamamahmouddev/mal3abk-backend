using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.Modules.Matches.Infrastructure.Jobs;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Matches.Application.Matches.ResolveDispute;

public sealed class ResolveMatchDisputeHandler : ICommandHandler<ResolveMatchDisputeCommand, Result<MatchDto>>
{
    private readonly IMatchesDbContext _dbContext;
    private readonly ITeamCaptaincyChecker _captaincyChecker;
    private readonly RankingRecalculationJob _recalculationJob;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IClock _clock;
    private readonly ILogger<ResolveMatchDisputeHandler> _logger;

    public ResolveMatchDisputeHandler(
        IMatchesDbContext dbContext,
        ITeamCaptaincyChecker captaincyChecker,
        RankingRecalculationJob recalculationJob,
        INotificationPublisher notificationPublisher,
        IClock clock,
        ILogger<ResolveMatchDisputeHandler> logger)
    {
        _dbContext = dbContext;
        _captaincyChecker = captaincyChecker;
        _recalculationJob = recalculationJob;
        _notificationPublisher = notificationPublisher;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<MatchDto>> HandleAsync(ResolveMatchDisputeCommand command, CancellationToken cancellationToken = default)
    {
        var match = await _dbContext.Matches
            .FirstOrDefaultAsync(m => m.Id == command.MatchId, cancellationToken);

        if (match is null)
        {
            return Result.Failure<MatchDto>(MatchErrors.NotFound);
        }

        var resolveResult = match.ResolveDispute(
            command.Action,
            command.ResolutionNotes,
            command.AdminUserId,
            command.OfficialHomeScore,
            command.OfficialAwayScore,
            _clock.UtcNow);

        if (!resolveResult.IsSuccess)
        {
            return Result.Failure<MatchDto>(resolveResult.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Recalculate rankings across confirmed matches to ensure rankings reflect corrected ruling
        try
        {
            await _recalculationJob.ExecuteRecalculationAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recalculation following dispute resolution on match {MatchId} logged warning", match.Id);
        }

        var homeTeamName = await _captaincyChecker.GetTeamNameAsync(match.HomeTeamId, cancellationToken) ?? "Home Team";
        var awayTeamName = await _captaincyChecker.GetTeamNameAsync(match.AwayTeamId, cancellationToken) ?? "Away Team";
        var homeCaptainId = await _captaincyChecker.GetCaptainPlayerIdAsync(match.HomeTeamId, cancellationToken);
        var awayCaptainId = await _captaincyChecker.GetCaptainPlayerIdAsync(match.AwayTeamId, cancellationToken);

        var decisionText = command.Action switch
        {
            MatchDisputeResolutionAction.ConfirmSubmittedScore => $"Submitted score {match.HomeScore}-{match.AwayScore} confirmed as official.",
            MatchDisputeResolutionAction.SetOfficialScore => $"Official score established: {match.HomeScore}-{match.AwayScore}.",
            MatchDisputeResolutionAction.CancelMatch => "Match cancelled/voided by administration.",
            _ => command.Action.ToString()
        };

        var notifData = new Dictionary<string, string>
        {
            ["matchId"] = match.Id.ToString("D"),
            ["homeTeam"] = homeTeamName,
            ["awayTeam"] = awayTeamName,
            ["decision"] = decisionText
        };

        var notifKey = $"dispute-resolved:{match.Id}:{command.Action}";
        if (homeCaptainId.HasValue && homeCaptainId.Value != Guid.Empty)
        {
            await _notificationPublisher.PublishAsync(
                new NotificationRequest(homeCaptainId.Value, NotificationTypes.MatchDisputeResolved, notifData, notifKey),
                cancellationToken);
        }

        if (awayCaptainId.HasValue && awayCaptainId.Value != Guid.Empty && awayCaptainId.Value != homeCaptainId)
        {
            await _notificationPublisher.PublishAsync(
                new NotificationRequest(awayCaptainId.Value, NotificationTypes.MatchDisputeResolved, notifData, notifKey),
                cancellationToken);
        }

        return Result.Success(MatchMapper.ToDto(match, homeTeamName, awayTeamName));
    }
}
