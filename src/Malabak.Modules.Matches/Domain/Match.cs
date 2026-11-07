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
    public ResultVerificationStatus ResultStatus { get; private set; }
    public Guid? ResultSubmittedByTeamId { get; private set; }
    public Guid? ResultSubmittedByPlayerId { get; private set; }
    public DateTime? ResultSubmittedAtUtc { get; private set; }
    public Guid? ResultVerifiedByTeamId { get; private set; }
    public Guid? ResultVerifiedByPlayerId { get; private set; }
    public DateTime? ResultVerifiedAtUtc { get; private set; }
    public string? DisputeReason { get; private set; }
    public DateTime? DisputedAtUtc { get; private set; }
    public Guid? CancelledByTeamId { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? Notes { get; private set; }
    public Guid? AdminResolvedByUserId { get; private set; }
    public DateTime? AdminResolvedAtUtc { get; private set; }
    public string? AdminResolutionNotes { get; private set; }
    public string? AdminResolutionAction { get; private set; }

    private readonly List<MatchResultAuditRecord> _uncommittedAuditRecords = [];
    public IReadOnlyCollection<MatchResultAuditRecord> UncommittedAuditRecords => _uncommittedAuditRecords.AsReadOnly();

    public void ClearUncommittedAuditRecords()
    {
        _uncommittedAuditRecords.Clear();
    }

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

    public Result SubmitResult(int homeScore, int awayScore, Guid submittedByTeamId, Guid submittedByPlayerId, DateTime nowUtc)
    {
        if (submittedByTeamId != HomeTeamId && submittedByTeamId != AwayTeamId)
        {
            return Result.Failure(MatchErrors.NotAuthorizedCaptain);
        }

        if (homeScore < 0 || awayScore < 0 || homeScore > 99 || awayScore > 99)
        {
            return Result.Failure(MatchErrors.InvalidScore);
        }

        if (Status == MatchStatus.Cancelled || Status == MatchStatus.Completed)
        {
            return Result.Failure(MatchErrors.InvalidStatus);
        }

        if (ResultStatus != ResultVerificationStatus.None)
        {
            return Result.Failure(MatchErrors.ResultAlreadySubmitted);
        }

        var prevHomeScore = HomeScore;
        var prevAwayScore = AwayScore;
        var prevStatus = ResultStatus;

        HomeScore = homeScore;
        AwayScore = awayScore;
        ResultSubmittedByTeamId = submittedByTeamId;
        ResultSubmittedByPlayerId = submittedByPlayerId;
        ResultSubmittedAtUtc = nowUtc;
        ResultStatus = ResultVerificationStatus.PendingVerification;
        Status = MatchStatus.InProgress;
        UpdatedAtUtc = nowUtc;

        _uncommittedAuditRecords.Add(MatchResultAuditRecord.Create(
            Id,
            prevHomeScore,
            prevAwayScore,
            homeScore,
            awayScore,
            prevStatus,
            ResultVerificationStatus.PendingVerification,
            submittedByPlayerId,
            "SubmitResult",
            "Initial match result submission",
            nowUtc));

        RaiseDomainEvent(new MatchResultSubmittedDomainEvent(Id, submittedByTeamId, homeScore, awayScore));

        return Result.Success();
    }

    public Result VerifyResult(
        MatchVerificationAction action,
        Guid verifyingTeamId,
        Guid verifyingPlayerId,
        string? disputeReason,
        DateTime nowUtc)
    {
        if (verifyingTeamId != HomeTeamId && verifyingTeamId != AwayTeamId)
        {
            return Result.Failure(MatchErrors.NotAuthorizedCaptain);
        }

        if (ResultStatus != ResultVerificationStatus.PendingVerification)
        {
            return Result.Failure(MatchErrors.ResultNotPendingVerification);
        }

        if (verifyingTeamId == ResultSubmittedByTeamId)
        {
            return Result.Failure(MatchErrors.CannotVerifyOwnResult);
        }

        var prevStatus = ResultStatus;

        if (action == MatchVerificationAction.Confirm)
        {
            ResultStatus = ResultVerificationStatus.Confirmed;
            ResultVerifiedByTeamId = verifyingTeamId;
            ResultVerifiedByPlayerId = verifyingPlayerId;
            ResultVerifiedAtUtc = nowUtc;
            Status = MatchStatus.Completed;

            if (HomeScore!.Value > AwayScore!.Value)
            {
                Outcome = MatchOutcome.HomeWin;
            }
            else if (AwayScore.Value > HomeScore.Value)
            {
                Outcome = MatchOutcome.AwayWin;
            }
            else
            {
                Outcome = MatchOutcome.Draw;
            }

            UpdatedAtUtc = nowUtc;

            _uncommittedAuditRecords.Add(MatchResultAuditRecord.Create(
                Id,
                HomeScore,
                AwayScore,
                HomeScore,
                AwayScore,
                prevStatus,
                ResultVerificationStatus.Confirmed,
                verifyingPlayerId,
                "Confirm",
                "Opposing captain confirmed match score",
                nowUtc));

            RaiseDomainEvent(new MatchResultConfirmedDomainEvent(
                Id,
                HomeTeamId,
                AwayTeamId,
                HomeScore.Value,
                AwayScore.Value,
                Outcome));

            return Result.Success();
        }

        if (action == MatchVerificationAction.Dispute)
        {
            if (string.IsNullOrWhiteSpace(disputeReason))
            {
                return Result.Failure(MatchErrors.DisputeReasonRequired);
            }

            ResultStatus = ResultVerificationStatus.Disputed;
            ResultVerifiedByTeamId = verifyingTeamId;
            ResultVerifiedByPlayerId = verifyingPlayerId;
            DisputeReason = disputeReason.Trim();
            DisputedAtUtc = nowUtc;
            Status = MatchStatus.Disputed;
            Outcome = MatchOutcome.None;
            UpdatedAtUtc = nowUtc;

            _uncommittedAuditRecords.Add(MatchResultAuditRecord.Create(
                Id,
                HomeScore,
                AwayScore,
                HomeScore,
                AwayScore,
                prevStatus,
                ResultVerificationStatus.Disputed,
                verifyingPlayerId,
                "Dispute",
                disputeReason.Trim(),
                nowUtc));

            RaiseDomainEvent(new MatchResultDisputedDomainEvent(
                Id,
                HomeTeamId,
                AwayTeamId,
                verifyingTeamId,
                DisputeReason));

            return Result.Success();
        }

        return Result.Failure(MatchErrors.InvalidStatus);
    }

    public Result AutoConfirmResult(DateTime nowUtc)
    {
        if (ResultStatus != ResultVerificationStatus.PendingVerification)
        {
            return Result.Failure(MatchErrors.ResultNotPendingVerification);
        }

        var prevStatus = ResultStatus;
        ResultStatus = ResultVerificationStatus.AutoConfirmed;
        Status = MatchStatus.Completed;
        ResultVerifiedAtUtc = nowUtc;

        if (HomeScore!.Value > AwayScore!.Value)
        {
            Outcome = MatchOutcome.HomeWin;
        }
        else if (AwayScore.Value > HomeScore.Value)
        {
            Outcome = MatchOutcome.AwayWin;
        }
        else
        {
            Outcome = MatchOutcome.Draw;
        }

        UpdatedAtUtc = nowUtc;

        _uncommittedAuditRecords.Add(MatchResultAuditRecord.Create(
            Id,
            HomeScore,
            AwayScore,
            HomeScore,
            AwayScore,
            prevStatus,
            ResultVerificationStatus.AutoConfirmed,
            Guid.Empty,
            "AutoConfirmation",
            "Auto-confirmed by system policy after verification timeout",
            nowUtc));

        RaiseDomainEvent(new MatchResultConfirmedDomainEvent(
            Id,
            HomeTeamId,
            AwayTeamId,
            HomeScore.Value,
            AwayScore.Value,
            Outcome));

        return Result.Success();
    }

    /// <summary>
    /// Admin ranking correction (TASK-705): fixes the score of a match whose result was already confirmed (e.g. a later-proven
    /// wrong result). Rankings must be recalculated afterwards; the change is kept in the result audit trail.
    /// </summary>
    public Result CorrectResult(int homeScore, int awayScore, Guid adminUserId, string reason, DateTime nowUtc)
    {
        if (Status != MatchStatus.Completed || ResultStatus is not (ResultVerificationStatus.Confirmed or ResultVerificationStatus.AutoConfirmed))
        {
            return Result.Failure(Error.Conflict("Match.ResultNotConfirmed", "Only a confirmed result can be corrected."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(MatchErrors.ResolutionNotesRequired);
        }

        if (homeScore < 0 || awayScore < 0 || homeScore > 99 || awayScore > 99)
        {
            return Result.Failure(MatchErrors.OfficialScoresRequired);
        }

        var prevHomeScore = HomeScore;
        var prevAwayScore = AwayScore;
        HomeScore = homeScore;
        AwayScore = awayScore;
        if (homeScore > awayScore)
        {
            Outcome = MatchOutcome.HomeWin;
        }
        else if (awayScore > homeScore)
        {
            Outcome = MatchOutcome.AwayWin;
        }
        else
        {
            Outcome = MatchOutcome.Draw;
        }

        AdminResolvedByUserId = adminUserId;
        AdminResolvedAtUtc = nowUtc;
        AdminResolutionNotes = reason.Trim();
        AdminResolutionAction = "CorrectResult";
        UpdatedAtUtc = nowUtc;
        _uncommittedAuditRecords.Add(MatchResultAuditRecord.Create(
            Id, prevHomeScore, prevAwayScore, HomeScore, AwayScore, ResultStatus, ResultStatus, adminUserId, "AdminCorrection",
            $"Result corrected. Reason: {reason.Trim()}", nowUtc));
        return Result.Success();
    }

    public Result ResolveDispute(
        MatchDisputeResolutionAction action,
        string resolutionNotes,
        Guid adminUserId,
        int? officialHomeScore,
        int? officialAwayScore,
        DateTime nowUtc)
    {
        if (Status != MatchStatus.Disputed && ResultStatus != ResultVerificationStatus.Disputed)
        {
            return Result.Failure(MatchErrors.MatchNotDisputed);
        }

        if (string.IsNullOrWhiteSpace(resolutionNotes))
        {
            return Result.Failure(MatchErrors.ResolutionNotesRequired);
        }

        var prevHomeScore = HomeScore;
        var prevAwayScore = AwayScore;
        var prevStatus = ResultStatus;

        if (action == MatchDisputeResolutionAction.SetOfficialScore)
        {
            if (!officialHomeScore.HasValue || !officialAwayScore.HasValue ||
                officialHomeScore.Value < 0 || officialAwayScore.Value < 0)
            {
                return Result.Failure(MatchErrors.OfficialScoresRequired);
            }

            HomeScore = officialHomeScore.Value;
            AwayScore = officialAwayScore.Value;
            ResultStatus = ResultVerificationStatus.Confirmed;
            Status = MatchStatus.Completed;

            if (HomeScore.Value > AwayScore.Value)
            {
                Outcome = MatchOutcome.HomeWin;
            }
            else if (AwayScore.Value > HomeScore.Value)
            {
                Outcome = MatchOutcome.AwayWin;
            }
            else
            {
                Outcome = MatchOutcome.Draw;
            }
        }
        else if (action == MatchDisputeResolutionAction.ConfirmSubmittedScore)
        {
            if (!HomeScore.HasValue || !AwayScore.HasValue)
            {
                return Result.Failure(MatchErrors.OfficialScoresRequired);
            }

            ResultStatus = ResultVerificationStatus.Confirmed;
            Status = MatchStatus.Completed;

            if (HomeScore.Value > AwayScore.Value)
            {
                Outcome = MatchOutcome.HomeWin;
            }
            else if (AwayScore.Value > HomeScore.Value)
            {
                Outcome = MatchOutcome.AwayWin;
            }
            else
            {
                Outcome = MatchOutcome.Draw;
            }
        }
        else if (action == MatchDisputeResolutionAction.CancelMatch)
        {
            Status = MatchStatus.Cancelled;
            ResultStatus = ResultVerificationStatus.None;
            Outcome = MatchOutcome.None;
            CancellationReason = resolutionNotes.Trim();
        }
        else
        {
            return Result.Failure(MatchErrors.InvalidResolutionAction);
        }

        AdminResolvedByUserId = adminUserId;
        AdminResolvedAtUtc = nowUtc;
        AdminResolutionNotes = resolutionNotes.Trim();
        AdminResolutionAction = action.ToString();
        UpdatedAtUtc = nowUtc;

        _uncommittedAuditRecords.Add(MatchResultAuditRecord.Create(
            Id,
            prevHomeScore,
            prevAwayScore,
            HomeScore,
            AwayScore,
            prevStatus,
            ResultStatus,
            adminUserId,
            "AdminResolution",
            $"Resolution: {action}. Notes: {resolutionNotes.Trim()}",
            nowUtc));

        RaiseDomainEvent(new MatchDisputeResolvedDomainEvent(
            Id,
            HomeTeamId,
            AwayTeamId,
            action,
            resolutionNotes.Trim()));

        if (ResultStatus == ResultVerificationStatus.Confirmed)
        {
            RaiseDomainEvent(new MatchResultConfirmedDomainEvent(
                Id,
                HomeTeamId,
                AwayTeamId,
                HomeScore!.Value,
                AwayScore!.Value,
                Outcome));
        }

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
