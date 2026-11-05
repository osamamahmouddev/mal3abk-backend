using Malabak.Modules.Matches.Domain.Events;
using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Domain;

public sealed class TeamChallenge : Entity<Guid>
{
    public const int MinDurationMinutes = 30;
    public const int MaxDurationMinutes = 180;
    public const int DefaultDurationMinutes = 60;

    public Guid ChallengerTeamId { get; private set; }
    public Guid ChallengedTeamId { get; private set; }
    public Guid ChallengerCaptainId { get; private set; }
    public DateTime ProposedDateUtc { get; private set; }
    public int ProposedDurationMinutes { get; private set; } = DefaultDurationMinutes;
    public Guid? ProposedPitchId { get; private set; }
    public Guid? ProposedVenueId { get; private set; }
    public string? ProposedVenueName { get; private set; }
    public string? Notes { get; private set; }
    public ChallengeStatus Status { get; private set; } = ChallengeStatus.Pending;
    public Guid CurrentTurnTeamId { get; private set; }
    public DateTime? AcceptedAtUtc { get; private set; }
    public DateTime? DeclinedAtUtc { get; private set; }
    public string? DeclinedReason { get; private set; }
    public Guid? MatchId { get; private set; }
    public Guid? BookingId { get; private set; }

    private TeamChallenge()
    {
    }

    public static Result<TeamChallenge> Create(
        Guid id,
        Guid challengerTeamId,
        Guid challengedTeamId,
        Guid challengerCaptainId,
        DateTime proposedDateUtc,
        DateTime nowUtc,
        int durationMinutes = DefaultDurationMinutes,
        Guid? proposedPitchId = null,
        Guid? proposedVenueId = null,
        string? proposedVenueName = null,
        string? notes = null)
    {
        if (challengerTeamId == challengedTeamId)
        {
            return Result.Failure<TeamChallenge>(ChallengeErrors.CannotChallengeSelf);
        }

        if (proposedDateUtc <= nowUtc)
        {
            return Result.Failure<TeamChallenge>(ChallengeErrors.DateInPast);
        }

        if (durationMinutes is < MinDurationMinutes or > MaxDurationMinutes)
        {
            return Result.Failure<TeamChallenge>(ChallengeErrors.InvalidDuration);
        }

        var challenge = new TeamChallenge
        {
            Id = id,
            ChallengerTeamId = challengerTeamId,
            ChallengedTeamId = challengedTeamId,
            ChallengerCaptainId = challengerCaptainId,
            ProposedDateUtc = proposedDateUtc,
            ProposedDurationMinutes = durationMinutes,
            ProposedPitchId = proposedPitchId,
            ProposedVenueId = proposedVenueId,
            ProposedVenueName = proposedVenueName?.Trim(),
            Notes = notes?.Trim(),
            Status = ChallengeStatus.Pending,
            CurrentTurnTeamId = challengedTeamId,
            CreatedAtUtc = nowUtc
        };

        challenge.RaiseDomainEvent(new ChallengeSentDomainEvent(
            challenge.Id,
            challengerTeamId,
            challengedTeamId,
            challengerCaptainId,
            proposedDateUtc,
            proposedPitchId,
            proposedVenueId));

        return Result.Success(challenge);
    }

    public Result CounterPropose(
        Guid respondingTeamId,
        Guid respondingCaptainId,
        DateTime newProposedDateUtc,
        DateTime nowUtc,
        int newDurationMinutes = DefaultDurationMinutes,
        Guid? newProposedPitchId = null,
        Guid? newProposedVenueId = null,
        string? newProposedVenueName = null,
        string? newNotes = null)
    {
        if (Status != ChallengeStatus.Pending && Status != ChallengeStatus.CounterProposed)
        {
            return Result.Failure(ChallengeErrors.InvalidStatus);
        }

        if (respondingTeamId != CurrentTurnTeamId)
        {
            return Result.Failure(ChallengeErrors.NotYourTurn);
        }

        if (newProposedDateUtc <= nowUtc)
        {
            return Result.Failure(ChallengeErrors.DateInPast);
        }

        if (newDurationMinutes is < MinDurationMinutes or > MaxDurationMinutes)
        {
            return Result.Failure(ChallengeErrors.InvalidDuration);
        }

        ProposedDateUtc = newProposedDateUtc;
        ProposedDurationMinutes = newDurationMinutes;
        ProposedPitchId = newProposedPitchId;
        ProposedVenueId = newProposedVenueId;
        if (!string.IsNullOrWhiteSpace(newProposedVenueName))
        {
            ProposedVenueName = newProposedVenueName.Trim();
        }

        if (newNotes is not null)
        {
            Notes = newNotes.Trim();
        }

        Status = ChallengeStatus.CounterProposed;
        // Turn switches to the other team
        CurrentTurnTeamId = respondingTeamId == ChallengerTeamId ? ChallengedTeamId : ChallengerTeamId;
        SetUpdatedAt(nowUtc);

        RaiseDomainEvent(new ChallengeCounterProposedDomainEvent(
            Id,
            respondingTeamId,
            respondingCaptainId,
            newProposedDateUtc,
            newProposedPitchId,
            newProposedVenueId));

        return Result.Success();
    }

    public Result Accept(Guid acceptingTeamId, Guid acceptingCaptainId, DateTime nowUtc)
    {
        if (Status != ChallengeStatus.Pending && Status != ChallengeStatus.CounterProposed)
        {
            return Result.Failure(ChallengeErrors.InvalidStatus);
        }

        if (acceptingTeamId != CurrentTurnTeamId)
        {
            return Result.Failure(ChallengeErrors.NotYourTurn);
        }

        if (ProposedDateUtc <= nowUtc)
        {
            return Result.Failure(ChallengeErrors.ChallengeExpired);
        }

        Status = ChallengeStatus.Accepted;
        AcceptedAtUtc = nowUtc;
        SetUpdatedAt(nowUtc);

        // Core requirement: Challenge acceptance does NOT automatically book a pitch
        RaiseDomainEvent(new ChallengeAcceptedDomainEvent(
            Id,
            ChallengerTeamId,
            ChallengedTeamId,
            acceptingCaptainId,
            ProposedDateUtc,
            ProposedPitchId,
            ProposedVenueId));

        return Result.Success();
    }

    public Result Decline(Guid decliningTeamId, Guid decliningCaptainId, string? reason, DateTime nowUtc)
    {
        if (Status != ChallengeStatus.Pending && Status != ChallengeStatus.CounterProposed)
        {
            return Result.Failure(ChallengeErrors.InvalidStatus);
        }

        if (decliningTeamId != ChallengerTeamId && decliningTeamId != ChallengedTeamId)
        {
            return Result.Failure(ChallengeErrors.NotAuthorizedCaptain);
        }

        Status = ChallengeStatus.Declined;
        DeclinedAtUtc = nowUtc;
        DeclinedReason = reason?.Trim();
        SetUpdatedAt(nowUtc);

        RaiseDomainEvent(new ChallengeDeclinedDomainEvent(Id, decliningTeamId, decliningCaptainId, DeclinedReason));

        return Result.Success();
    }

    public Result Cancel(Guid cancellingTeamId, Guid cancellingCaptainId, DateTime nowUtc)
    {
        if (Status != ChallengeStatus.Pending && Status != ChallengeStatus.CounterProposed)
        {
            return Result.Failure(ChallengeErrors.InvalidStatus);
        }

        // Only the team that made the last offer can cancel
        var lastOfferedByTeamId = CurrentTurnTeamId == ChallengerTeamId ? ChallengedTeamId : ChallengerTeamId;
        if (cancellingTeamId != lastOfferedByTeamId)
        {
            return Result.Failure(ChallengeErrors.NotAuthorizedCaptain);
        }

        Status = ChallengeStatus.Cancelled;
        SetUpdatedAt(nowUtc);

        RaiseDomainEvent(new ChallengeCancelledDomainEvent(Id, cancellingTeamId, cancellingCaptainId));

        return Result.Success();
    }

    public void LinkMatch(Guid matchId, DateTime nowUtc)
    {
        MatchId = matchId;
        SetUpdatedAt(nowUtc);
    }

    public Result LinkBooking(Guid bookingId, Guid callerTeamId, DateTime nowUtc)
    {
        if (Status != ChallengeStatus.Accepted)
        {
            return Result.Failure(ChallengeErrors.InvalidStatus);
        }

        if (callerTeamId != ChallengerTeamId && callerTeamId != ChallengedTeamId)
        {
            return Result.Failure(ChallengeErrors.NotAuthorizedCaptain);
        }

        if (bookingId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("Challenge.InvalidBookingId", "Booking ID cannot be empty."));
        }

        BookingId = bookingId;
        SetUpdatedAt(nowUtc);

        return Result.Success();
    }

    public string GetBookingDeepLink()
    {
        var pitchParam = ProposedPitchId.HasValue ? $"&pitchId={ProposedPitchId.Value:D}" : string.Empty;
        var venueParam = ProposedVenueId.HasValue ? $"&venueId={ProposedVenueId.Value:D}" : string.Empty;
        return $"malabak://booking/create?challengeId={Id:D}{venueParam}{pitchParam}&date={Uri.EscapeDataString(ProposedDateUtc.ToString("o"))}&duration={ProposedDurationMinutes}";
    }
}
