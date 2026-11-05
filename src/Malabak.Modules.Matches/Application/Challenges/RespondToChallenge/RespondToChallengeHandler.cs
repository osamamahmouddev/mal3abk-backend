using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Matches.Application.Challenges.RespondToChallenge;

public sealed class RespondToChallengeHandler : ICommandHandler<RespondToChallengeCommand, Result<TeamChallengeDto>>
{
    private readonly IMatchesDbContext _dbContext;
    private readonly ITeamCaptaincyChecker _captaincyChecker;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IClock _clock;

    public RespondToChallengeHandler(
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

    public async Task<Result<TeamChallengeDto>> HandleAsync(RespondToChallengeCommand command, CancellationToken cancellationToken = default)
    {
        var isCaptain = await _captaincyChecker.IsCaptainOrViceCaptainAsync(command.RespondingTeamId, command.CallerPlayerId, cancellationToken);
        if (!isCaptain)
        {
            return Result.Failure<TeamChallengeDto>(ChallengeErrors.NotAuthorizedCaptain);
        }

        var challenge = await _dbContext.TeamChallenges
            .FirstOrDefaultAsync(c => c.Id == command.ChallengeId, cancellationToken);

        if (challenge is null)
        {
            return Result.Failure<TeamChallengeDto>(ChallengeErrors.NotFound);
        }

        Result actionResult = command.Action switch
        {
            ChallengeResponseAction.Accept => challenge.Accept(command.RespondingTeamId, command.CallerPlayerId, _clock.UtcNow),
            ChallengeResponseAction.Decline => challenge.Decline(command.RespondingTeamId, command.CallerPlayerId, command.DeclineReason, _clock.UtcNow),
            ChallengeResponseAction.CounterPropose => challenge.CounterPropose(
                command.RespondingTeamId,
                command.CallerPlayerId,
                command.NewProposedDateUtc!.Value,
                _clock.UtcNow,
                command.NewDurationMinutes ?? TeamChallenge.DefaultDurationMinutes,
                command.NewProposedPitchId,
                command.NewProposedVenueId,
                command.NewProposedVenueName,
                command.Notes),
            _ => Result.Failure(ChallengeErrors.InvalidStatus)
        };

        if (!actionResult.IsSuccess)
        {
            return Result.Failure<TeamChallengeDto>(actionResult.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Notify other team's captain on counter-propose or accept
        var otherTeamId = command.RespondingTeamId == challenge.ChallengerTeamId
            ? challenge.ChallengedTeamId
            : challenge.ChallengerTeamId;

        var otherCaptainId = await _captaincyChecker.GetCaptainPlayerIdAsync(otherTeamId, cancellationToken);
        var respondingTeamName = await _captaincyChecker.GetTeamNameAsync(command.RespondingTeamId, cancellationToken) ?? "Opponent Team";
        var otherTeamName = await _captaincyChecker.GetTeamNameAsync(otherTeamId, cancellationToken) ?? "Your Team";

        if (otherCaptainId.HasValue && otherCaptainId.Value != Guid.Empty && command.Action == ChallengeResponseAction.CounterPropose)
        {
            await _notificationPublisher.PublishAsync(
                new NotificationRequest(
                    otherCaptainId.Value,
                    NotificationTypes.TeamChallengeReceived,
                    new Dictionary<string, string>
                    {
                        ["teamName"] = respondingTeamName,
                        ["teamId"] = command.RespondingTeamId.ToString("D"),
                        ["challengeId"] = challenge.Id.ToString("D")
                    },
                    $"challenge-counter:{challenge.Id}:{challenge.ProposedDateUtc:yyyyMMddHHmm}"),
                cancellationToken);
        }

        var challengerName = command.RespondingTeamId == challenge.ChallengerTeamId ? respondingTeamName : otherTeamName;
        var challengedName = command.RespondingTeamId == challenge.ChallengedTeamId ? respondingTeamName : otherTeamName;

        return Result.Success(ChallengeMapper.ToDto(
            challenge,
            challengerName,
            challengedName,
            command.RespondingTeamId));
    }
}
