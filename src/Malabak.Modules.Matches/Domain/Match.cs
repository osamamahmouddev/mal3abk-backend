using Malabak.Modules.Matches.Domain.Events;
using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Domain;

public sealed class Match : Entity<Guid>
{
    public const int DefaultDurationMinutes = 60;
    public const int MinDurationMinutes = 30;
    public const int MaxDurationMinutes = 240;

    public Guid? ChallengeId { get; private set; }
    public Guid HomeTeamId { get; private set; }
    public Guid AwayTeamId { get; private set; }
    public Guid? PitchId { get; private set; }
    public Guid? VenueId { get; private set; }
    public string? VenueName { get; private set; }
    public DateTime ScheduledStartUtc { get; private set; }
    public DateTime ScheduledEndUtc { get; private set; }
    public int DurationMinutes { get; private set; }
    public Guid? BookingId { get; private set; }
    public MatchStatus Status { get; private set; }
    public MatchOutcome Outcome { get; private set; }
    public int? HomeScore { get; internal set; }
    public int? AwayScore { get; internal set; }
    public Guid? CancelledByTeamId { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? Notes { get; private set; }

    private Match()
    {
    }

    public static Result<Match> Schedule(
        Guid? challengeId,
        Guid homeTeamId,
        Guid awayTeamId,
        DateTime scheduledStartUtc,
        int durationMinutes,
        Guid? pitchId,
        Guid? venueId,
        string? venueName,
        Guid? bookingId,
        string? notes,
        DateTime nowUtc)
    {
        if (homeTeamId == Guid.Empty || awayTeamId == Guid.Empty)
        {
            return Result.Failure<Match>(Error.Validation("Match.InvalidTeams", "Home and away team identifiers must not be empty."));
        }

        if (homeTeamId == awayTeamId)
        {
            return Result.Failure<Match>(MatchErrors.SameTeam);
        }

        if (durationMinutes < MinDurationMinutes || durationMinutes > MaxDurationMinutes)
        {
            return Result.Failure<Match>(MatchErrors.InvalidDuration);
        }

        if (scheduledStartUtc < nowUtc.AddMinutes(-5))
        {
            return Result.Failure<Match>(MatchErrors.InvalidScheduledTime);
        }

        var match = new Match
        {
            Id = Guid.NewGuid(),
            ChallengeId = challengeId,
            HomeTeamId = homeTeamId,
            AwayTeamId = awayTeamId,
            ScheduledStartUtc = scheduledStartUtc,
            DurationMinutes = durationMinutes,
            ScheduledEndUtc = scheduledStartUtc.AddMinutes(durationMinutes),
            PitchId = pitchId,
            VenueId = venueId,
            VenueName = venueName?.Trim(),
            BookingId = bookingId,
            Status = MatchStatus.Scheduled,
            Outcome = MatchOutcome.None,
            Notes = notes?.Trim(),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        match.RaiseDomainEvent(new MatchScheduledDomainEvent(match.Id, homeTeamId, awayTeamId, scheduledStartUtc));

        return Result.Success(match);
    }

    public Result LinkBooking(Guid bookingId, Guid callerTeamId, DateTime nowUtc)
    {
        if (callerTeamId != HomeTeamId && callerTeamId != AwayTeamId)
        {
            return Result.Failure(MatchErrors.NotAuthorizedCaptain);
        }

        if (Status != MatchStatus.Scheduled && Status != MatchStatus.InProgress)
        {
            return Result.Failure(MatchErrors.InvalidStatus);
        }

        if (bookingId == Guid.Empty)
        {
            return Result.Failure(MatchErrors.BookingMismatch);
        }

        BookingId = bookingId;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Cancel(Guid callerTeamId, string? reason, DateTime nowUtc)
    {
        if (callerTeamId != HomeTeamId && callerTeamId != AwayTeamId)
        {
            return Result.Failure(MatchErrors.NotAuthorizedCaptain);
        }

        if (Status != MatchStatus.Scheduled)
        {
            return Result.Failure(MatchErrors.InvalidStatus);
        }

        Status = MatchStatus.Cancelled;
        Outcome = MatchOutcome.Cancelled;
        CancelledByTeamId = callerTeamId;
        CancellationReason = reason?.Trim();
        UpdatedAtUtc = nowUtc;

        RaiseDomainEvent(new MatchCancelledDomainEvent(Id, callerTeamId, reason));

        return Result.Success();
    }

    public string GetBookingDeepLink()
    {
        var pitchParam = PitchId.HasValue ? $"&pitchId={PitchId.Value:D}" : string.Empty;
        var venueParam = VenueId.HasValue ? $"&venueId={VenueId.Value:D}" : string.Empty;
        var dateParam = $"&date={Uri.EscapeDataString(ScheduledStartUtc.ToString("O"))}";
        var durationParam = $"&duration={DurationMinutes}";

        return $"malabak://booking/create?matchId={Id:D}{venueParam}{pitchParam}{dateParam}{durationParam}";
    }
}
