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

        Match? createdMatch = null;
        if (command.Action == ChallengeResponseAction.Accept)
        {
            var proposedEnd = challenge.ProposedDateUtc.AddMinutes(challenge.ProposedDurationMinutes);
            var hasConflict = await _dbContext.Matches.AnyAsync(m =>
                (m.Status == MatchStatus.Scheduled || m.Status == MatchStatus.InProgress) &&
                (m.HomeTeamId == challenge.ChallengerTeamId || m.HomeTeamId == challenge.ChallengedTeamId ||
                 m.AwayTeamId == challenge.ChallengerTeamId || m.AwayTeamId == challenge.ChallengedTeamId) &&
                m.ScheduledStartUtc < proposedEnd &&
                challenge.ProposedDateUtc < m.ScheduledEndUtc,
                cancellationToken);

            if (hasConflict)
            {
                return Result.Failure<TeamChallengeDto>(MatchErrors.ScheduleConflict);
            }

            var matchScheduleResult = Match.Schedule(
                challengeId: challenge.Id,
                homeTeamId: challenge.ChallengerTeamId,
                awayTeamId: challenge.ChallengedTeamId,
                scheduledStartUtc: challenge.ProposedDateUtc,
                durationMinutes: challenge.ProposedDurationMinutes,
                pitchId: challenge.ProposedPitchId,
                venueId: challenge.ProposedVenueId,
                venueName: challenge.ProposedVenueName,
                bookingId: challenge.BookingId,
                notes: challenge.Notes,
                nowUtc: _clock.UtcNow);

            if (!matchScheduleResult.IsSuccess)
            {
                return Result.Failure<TeamChallengeDto>(matchScheduleResult.Error);
            }

            createdMatch = matchScheduleResult.Value;
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

        if (createdMatch is not null)
        {
            _dbContext.Matches.Add(createdMatch);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Notify other team's captain on counter-propose or accept
        var otherTeamId = command.RespondingTeamId == challenge.ChallengerTeamId
            ? challenge.ChallengedTeamId
            : challenge.ChallengerTeamId;

        var otherCaptainId = await _captaincyChecker.GetCaptainPlayerIdAsync(otherTeamId, cancellationToken);
        var respondingTeamName = await _captaincyChecker.GetTeamNameAsync(command.RespondingTeamId, cancellationToken) ?? "Opponent Team";
        var otherTeamName = await _captaincyChecker.GetTeamNameAsync(otherTeamId, cancellationToken) ?? "Your Team";

        if (otherCaptainId.HasValue && otherCaptainId.Value != Guid.Empty)
        {
            if (command.Action == ChallengeResponseAction.CounterPropose)
            {
                await _notificationPublisher.PublishAsync(
                    new NotificationRequest(
                        otherCaptainId.Value,
                        NotificationTypes.TeamChallengeCountered,
                        new Dictionary<string, string>
                        {
                            ["teamName"] = respondingTeamName,
                            ["newDate"] = challenge.ProposedDateUtc.ToString("yyyy-MM-dd HH:mm"),
                            ["teamId"] = command.RespondingTeamId.ToString("D"),
                            ["challengeId"] = challenge.Id.ToString("D")
                        },
                        $"challenge-counter:{challenge.Id}:{challenge.ProposedDateUtc:yyyyMMddHHmm}"),
                    cancellationToken);
            }
            else if (command.Action == ChallengeResponseAction.Decline)
            {
                await _notificationPublisher.PublishAsync(
                    new NotificationRequest(
                        otherCaptainId.Value,
                        NotificationTypes.TeamChallengeDeclined,
                        new Dictionary<string, string>
                        {
                            ["teamName"] = respondingTeamName,
                            ["reason"] = command.DeclineReason ?? "No reason specified",
                            ["teamId"] = command.RespondingTeamId.ToString("D"),
                            ["challengeId"] = challenge.Id.ToString("D")
                        },
                        $"challenge-decline:{challenge.Id}"),
                    cancellationToken);
            }
            else if (command.Action == ChallengeResponseAction.Accept && createdMatch is not null)
            {
                await _notificationPublisher.PublishAsync(
                    new NotificationRequest(
                        otherCaptainId.Value,
                        NotificationTypes.TeamChallengeAccepted,
                        new Dictionary<string, string>
                        {
                            ["teamName"] = respondingTeamName,
                            ["matchDate"] = createdMatch.ScheduledStartUtc.ToString("yyyy-MM-dd HH:mm"),
                            ["matchId"] = createdMatch.Id.ToString("D"),
                            ["teamId"] = command.RespondingTeamId.ToString("D")
                        },
                        $"challenge-accept:{challenge.Id}:{createdMatch.Id}"),
                    cancellationToken);

                await _notificationPublisher.PublishAsync(
                    new NotificationRequest(
                        otherCaptainId.Value,
                        NotificationTypes.MatchScheduled,
                        new Dictionary<string, string>
                        {
                            ["opponentTeam"] = respondingTeamName,
                            ["matchDate"] = createdMatch.ScheduledStartUtc.ToString("yyyy-MM-dd HH:mm"),
                            ["matchId"] = createdMatch.Id.ToString("D")
                        },
                        $"match-sched:{createdMatch.Id}:{otherCaptainId.Value}"),
                    cancellationToken);
            }
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
