using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;

namespace Malabak.Modules.Matches.Application.Challenges.SendChallenge;

public sealed class SendChallengeHandler : ICommandHandler<SendChallengeCommand, Result<TeamChallengeDto>>
{
    private readonly IMatchesDbContext _dbContext;
    private readonly ITeamCaptaincyChecker _captaincyChecker;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IClock _clock;

    public SendChallengeHandler(
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

    public async Task<Result<TeamChallengeDto>> HandleAsync(SendChallengeCommand command, CancellationToken cancellationToken = default)
    {
        var isCaptain = await _captaincyChecker.IsCaptainOrViceCaptainAsync(command.ChallengerTeamId, command.CallerPlayerId, cancellationToken);
        if (!isCaptain)
        {
            return Result.Failure<TeamChallengeDto>(ChallengeErrors.NotAuthorizedCaptain);
        }

        var canBeChallenged = await _captaincyChecker.CanBeChallengedAsync(command.ChallengedTeamId, cancellationToken);
        if (!canBeChallenged)
        {
            return Result.Failure<TeamChallengeDto>(ChallengeErrors.TargetNotOpenForChallenges);
        }

        var challengeId = Guid.NewGuid();
        var createResult = TeamChallenge.Create(
            challengeId,
            command.ChallengerTeamId,
            command.ChallengedTeamId,
            command.CallerPlayerId,
            command.ProposedDateUtc,
            _clock.UtcNow,
            command.DurationMinutes,
            command.ProposedPitchId,
            command.ProposedVenueId,
            command.ProposedVenueName,
            command.Notes);

        if (!createResult.IsSuccess)
        {
            return Result.Failure<TeamChallengeDto>(createResult.Error);
        }

        var challenge = createResult.Value;
        _dbContext.TeamChallenges.Add(challenge);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Fetch team names for notification and DTO
        var challengerName = await _captaincyChecker.GetTeamNameAsync(command.ChallengerTeamId, cancellationToken) ?? "Unknown Team";
        var challengedName = await _captaincyChecker.GetTeamNameAsync(command.ChallengedTeamId, cancellationToken) ?? "Unknown Team";

        // Dispatch push notification to opposing captain
        var challengedCaptainId = await _captaincyChecker.GetCaptainPlayerIdAsync(command.ChallengedTeamId, cancellationToken);
        if (challengedCaptainId.HasValue && challengedCaptainId.Value != Guid.Empty)
        {
            var notificationData = new Dictionary<string, string>
            {
                ["teamName"] = challengerName,
                ["teamId"] = command.ChallengerTeamId.ToString("D"),
                ["challengeId"] = challenge.Id.ToString("D")
            };

            await _notificationPublisher.PublishAsync(
                new NotificationRequest(
                    challengedCaptainId.Value,
                    NotificationTypes.TeamChallengeReceived,
                    notificationData,
                    $"challenge:{challenge.Id}"),
                cancellationToken);
        }

        return Result.Success(new TeamChallengeDto(
            challenge.Id,
            challenge.ChallengerTeamId,
            challengerName,
            challenge.ChallengedTeamId,
            challengedName,
            challenge.ChallengerCaptainId,
            challenge.ProposedDateUtc,
            challenge.ProposedDurationMinutes,
            challenge.ProposedPitchId,
            challenge.ProposedVenueId,
            challenge.ProposedVenueName,
            challenge.Notes,
            challenge.Status,
            challenge.CurrentTurnTeamId,
            IsMyTurn: false, // Turn is with challenged team
            challenge.AcceptedAtUtc,
            challenge.DeclinedAtUtc,
            challenge.DeclinedReason,
            challenge.MatchId,
            challenge.CreatedAtUtc));
    }
}
