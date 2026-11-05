using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Matches.Application.Matches.ScheduleMatch;

public sealed class ScheduleMatchHandler : ICommandHandler<ScheduleMatchCommand, Result<MatchDto>>
{
    private readonly IMatchesDbContext _dbContext;
    private readonly ITeamCaptaincyChecker _captaincyChecker;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IClock _clock;

    public ScheduleMatchHandler(
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

    public async Task<Result<MatchDto>> HandleAsync(ScheduleMatchCommand command, CancellationToken cancellationToken = default)
    {
        var isCaptain = await _captaincyChecker.IsCaptainOrViceCaptainAsync(command.HomeTeamId, command.CallerPlayerId, cancellationToken);
        if (!isCaptain)
        {
            return Result.Failure<MatchDto>(MatchErrors.NotAuthorizedCaptain);
        }

        var proposedStart = command.ScheduledStartUtc;
        var proposedEnd = proposedStart.AddMinutes(command.DurationMinutes);

        // Schedule conflict prevention:
        // Ensure neither HomeTeam nor AwayTeam is already playing in another active match during the proposed window.
        var hasConflict = await _dbContext.Matches.AnyAsync(m =>
            (m.Status == MatchStatus.Scheduled || m.Status == MatchStatus.InProgress) &&
            (m.HomeTeamId == command.HomeTeamId || m.HomeTeamId == command.AwayTeamId ||
             m.AwayTeamId == command.HomeTeamId || m.AwayTeamId == command.AwayTeamId) &&
            m.ScheduledStartUtc < proposedEnd &&
            proposedStart < m.ScheduledEndUtc,
            cancellationToken);

        if (hasConflict)
        {
            return Result.Failure<MatchDto>(MatchErrors.ScheduleConflict);
        }

        var matchResult = Match.Schedule(
            challengeId: null,
            homeTeamId: command.HomeTeamId,
            awayTeamId: command.AwayTeamId,
            scheduledStartUtc: command.ScheduledStartUtc,
            durationMinutes: command.DurationMinutes,
            pitchId: command.PitchId,
            venueId: command.VenueId,
            venueName: command.VenueName,
            bookingId: command.BookingId,
            notes: command.Notes,
            nowUtc: _clock.UtcNow);

        if (!matchResult.IsSuccess)
        {
            return Result.Failure<MatchDto>(matchResult.Error);
        }

        var match = matchResult.Value;
        _dbContext.Matches.Add(match);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var homeTeamName = await _captaincyChecker.GetTeamNameAsync(match.HomeTeamId, cancellationToken) ?? "Home Team";
        var awayTeamName = await _captaincyChecker.GetTeamNameAsync(match.AwayTeamId, cancellationToken) ?? "Away Team";

        // Dispatch notifications to both captains
        var homeCaptainId = await _captaincyChecker.GetCaptainPlayerIdAsync(match.HomeTeamId, cancellationToken);
        var awayCaptainId = await _captaincyChecker.GetCaptainPlayerIdAsync(match.AwayTeamId, cancellationToken);

        if (awayCaptainId.HasValue && awayCaptainId.Value != Guid.Empty)
        {
            await _notificationPublisher.PublishAsync(
                new NotificationRequest(
                    awayCaptainId.Value,
                    NotificationTypes.MatchScheduled,
                    new Dictionary<string, string>
                    {
                        ["opponentTeam"] = homeTeamName,
                        ["matchDate"] = match.ScheduledStartUtc.ToString("yyyy-MM-dd HH:mm"),
                        ["matchId"] = match.Id.ToString("D")
                    },
                    $"match-sched:{match.Id}:{awayCaptainId.Value}"),
                cancellationToken);
        }

        if (homeCaptainId.HasValue && homeCaptainId.Value != Guid.Empty && homeCaptainId.Value != command.CallerPlayerId)
        {
            await _notificationPublisher.PublishAsync(
                new NotificationRequest(
                    homeCaptainId.Value,
                    NotificationTypes.MatchScheduled,
                    new Dictionary<string, string>
                    {
                        ["opponentTeam"] = awayTeamName,
                        ["matchDate"] = match.ScheduledStartUtc.ToString("yyyy-MM-dd HH:mm"),
                        ["matchId"] = match.Id.ToString("D")
                    },
                    $"match-sched:{match.Id}:{homeCaptainId.Value}"),
                cancellationToken);
        }

        return Result.Success(MatchMapper.ToDto(match, homeTeamName, awayTeamName));
    }
}
