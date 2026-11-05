using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Matches.Application.Matches.SubmitResult;

public sealed class SubmitMatchResultHandler : ICommandHandler<SubmitMatchResultCommand, Result<MatchDto>>
{
    private readonly IMatchesDbContext _dbContext;
    private readonly ITeamCaptaincyChecker _captaincyChecker;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IClock _clock;

    public SubmitMatchResultHandler(
        IMatchesDbContext dbContext,
        ITeamCaptaincyChecker captaincyChecker,
        INotificationPublisher notificationPublisher,
        IClock clock)
    {
        _dbContext = dbContext;
        _captaincyChecker = captaincyChecker;
        _notificationPublisher = notificationPublisher;
        _clock = clock;
    }

    public async Task<Result<MatchDto>> HandleAsync(SubmitMatchResultCommand command, CancellationToken cancellationToken = default)
    {
        var isCaptain = await _captaincyChecker.IsCaptainOrViceCaptainAsync(command.SubmittingTeamId, command.CallerPlayerId, cancellationToken);
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

        var submitResult = match.SubmitResult(
            command.HomeScore,
            command.AwayScore,
            command.SubmittingTeamId,
            command.CallerPlayerId,
            _clock.UtcNow);

        if (!submitResult.IsSuccess)
        {
            return Result.Failure<MatchDto>(submitResult.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var homeTeamName = await _captaincyChecker.GetTeamNameAsync(match.HomeTeamId, cancellationToken) ?? "Home Team";
        var awayTeamName = await _captaincyChecker.GetTeamNameAsync(match.AwayTeamId, cancellationToken) ?? "Away Team";

        // Notify opponent team's captain about the pending verification
        var opponentTeamId = command.SubmittingTeamId == match.HomeTeamId
            ? match.AwayTeamId
            : match.HomeTeamId;

        var opponentCaptainId = await _captaincyChecker.GetCaptainPlayerIdAsync(opponentTeamId, cancellationToken);
        var submittingTeamName = command.SubmittingTeamId == match.HomeTeamId ? homeTeamName : awayTeamName;

        if (opponentCaptainId.HasValue && opponentCaptainId.Value != Guid.Empty)
        {
            await _notificationPublisher.PublishAsync(
                new NotificationRequest(
                    opponentCaptainId.Value,
                    NotificationTypes.MatchResultSubmitted,
                    new Dictionary<string, string>
                    {
                        ["submittingTeam"] = submittingTeamName,
                        ["score"] = $"{command.HomeScore} - {command.AwayScore}",
                        ["matchId"] = match.Id.ToString("D")
                    },
                    $"match-res-submit:{match.Id}:{command.HomeScore}-{command.AwayScore}"),
                cancellationToken);
        }

        return Result.Success(MatchMapper.ToDto(match, homeTeamName, awayTeamName));
    }
}
