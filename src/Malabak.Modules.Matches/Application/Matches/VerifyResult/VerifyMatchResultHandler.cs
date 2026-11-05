using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Matches.Application.Matches.VerifyResult;

public sealed class VerifyMatchResultHandler : ICommandHandler<VerifyMatchResultCommand, Result<MatchDto>>
{
    private readonly IMatchesDbContext _dbContext;
    private readonly ITeamCaptaincyChecker _captaincyChecker;
    private readonly ITeamMatchResultRecorder _teamMatchResultRecorder;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IDomainEventDispatcher? _domainEventDispatcher;
    private readonly IClock _clock;

    public VerifyMatchResultHandler(
        IMatchesDbContext dbContext,
        ITeamCaptaincyChecker captaincyChecker,
        ITeamMatchResultRecorder teamMatchResultRecorder,
        INotificationPublisher notificationPublisher,
        IClock clock,
        IDomainEventDispatcher? domainEventDispatcher = null)
    {
        _dbContext = dbContext;
        _captaincyChecker = captaincyChecker;
        _teamMatchResultRecorder = teamMatchResultRecorder;
        _notificationPublisher = notificationPublisher;
        _clock = clock;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public async Task<Result<MatchDto>> HandleAsync(VerifyMatchResultCommand command, CancellationToken cancellationToken = default)
    {
        var isCaptain = await _captaincyChecker.IsCaptainOrViceCaptainAsync(command.VerifyingTeamId, command.CallerPlayerId, cancellationToken);
        if (!isCaptain)
        {
            return Result.Failure<MatchDto>(MatchErrors.NotAuthorizedCaptain);
        }

        var match = await _dbContext.Matches
            .FirstOrDefaultAsync(m => m.Id == command.MatchId, cancellationToken);

        if (match is null)
        {
            return Result.Failure<MatchDto>(MatchErrors.NotFound);
        }

        var verifyResult = match.VerifyResult(
            command.Action,
            command.VerifyingTeamId,
            command.CallerPlayerId,
            command.DisputeReason,
            _clock.UtcNow);

        if (!verifyResult.IsSuccess)
        {
            return Result.Failure<MatchDto>(verifyResult.Error);
        }

        // Confirmed results become official and update team stats via event handler or recorder
        if (command.Action == MatchVerificationAction.Confirm)
        {
            if (_domainEventDispatcher is not null)
            {
                await _domainEventDispatcher.DispatchAsync(match.DomainEvents, cancellationToken);
            }
            else
            {
                await _teamMatchResultRecorder.RecordConfirmedResultAsync(
                    match.HomeTeamId,
                    match.AwayTeamId,
                    match.HomeScore!.Value,
                    match.AwayScore!.Value,
                    cancellationToken);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var homeTeamName = await _captaincyChecker.GetTeamNameAsync(match.HomeTeamId, cancellationToken) ?? "Home Team";
        var awayTeamName = await _captaincyChecker.GetTeamNameAsync(match.AwayTeamId, cancellationToken) ?? "Away Team";
        var homeCaptainId = await _captaincyChecker.GetCaptainPlayerIdAsync(match.HomeTeamId, cancellationToken);
        var awayCaptainId = await _captaincyChecker.GetCaptainPlayerIdAsync(match.AwayTeamId, cancellationToken);

        var scoreText = $"{match.HomeScore} - {match.AwayScore}";

        if (command.Action == MatchVerificationAction.Confirm)
        {
            var notifKey = $"match-res-confirm:{match.Id}:{scoreText}";
            if (homeCaptainId.HasValue && homeCaptainId.Value != Guid.Empty)
            {
                await _notificationPublisher.PublishAsync(
                    new NotificationRequest(
                        homeCaptainId.Value,
                        NotificationTypes.MatchResultConfirmed,
                        new Dictionary<string, string> { ["score"] = scoreText, ["matchId"] = match.Id.ToString("D") },
                        notifKey),
                    cancellationToken);
            }

            if (awayCaptainId.HasValue && awayCaptainId.Value != Guid.Empty && awayCaptainId.Value != homeCaptainId)
            {
                await _notificationPublisher.PublishAsync(
                    new NotificationRequest(
                        awayCaptainId.Value,
                        NotificationTypes.MatchResultConfirmed,
                        new Dictionary<string, string> { ["score"] = scoreText, ["matchId"] = match.Id.ToString("D") },
                        notifKey),
                    cancellationToken);
            }
        }
        else if (command.Action == MatchVerificationAction.Dispute)
        {
            var disputingTeamName = command.VerifyingTeamId == match.HomeTeamId ? homeTeamName : awayTeamName;
            var otherCaptainId = command.VerifyingTeamId == match.HomeTeamId ? awayCaptainId : homeCaptainId;

            if (otherCaptainId.HasValue && otherCaptainId.Value != Guid.Empty)
            {
                await _notificationPublisher.PublishAsync(
                    new NotificationRequest(
                        otherCaptainId.Value,
                        NotificationTypes.MatchResultDisputed,
                        new Dictionary<string, string>
                        {
                            ["disputingTeam"] = disputingTeamName,
                            ["matchId"] = match.Id.ToString("D")
                        },
                        $"match-res-dispute:{match.Id}:{command.VerifyingTeamId}"),
                    cancellationToken);
            }
        }

        return Result.Success(MatchMapper.ToDto(match, homeTeamName, awayTeamName));
    }
}
