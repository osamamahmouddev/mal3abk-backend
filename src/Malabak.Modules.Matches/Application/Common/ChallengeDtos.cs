using Malabak.Modules.Matches.Domain;

namespace Malabak.Modules.Matches.Application.Common;

public sealed record TeamChallengeDto(
    Guid Id,
    Guid ChallengerTeamId,
    string? ChallengerTeamName,
    Guid ChallengedTeamId,
    string? ChallengedTeamName,
    Guid ChallengerCaptainId,
    DateTime ProposedDateUtc,
    int ProposedDurationMinutes,
    Guid? ProposedPitchId,
    Guid? ProposedVenueId,
    string? ProposedVenueName,
    string? Notes,
    ChallengeStatus Status,
    Guid CurrentTurnTeamId,
    bool IsMyTurn,
    DateTime? AcceptedAtUtc,
    DateTime? DeclinedAtUtc,
    string? DeclinedReason,
    Guid? MatchId,
    Guid? BookingId,
    string? BookingDeepLink,
    DateTime CreatedAtUtc);

public sealed record SendChallengeRequest(
    Guid ChallengerTeamId,
    Guid ChallengedTeamId,
    DateTime ProposedDateUtc,
    int DurationMinutes,
    Guid? ProposedPitchId,
    Guid? ProposedVenueId,
    string? ProposedVenueName,
    string? Notes);

public enum ChallengeResponseAction
{
    Accept = 1,
    Decline = 2,
    CounterPropose = 3
}

public sealed record RespondToChallengeRequest(
    Guid RespondingTeamId,
    ChallengeResponseAction Action,
    DateTime? NewProposedDateUtc,
    int? NewDurationMinutes,
    Guid? NewProposedPitchId,
    Guid? NewProposedVenueId,
    string? NewProposedVenueName,
    string? Notes,
    string? DeclineReason);

public sealed record LinkChallengeBookingRequest(
    Guid TeamId,
    Guid BookingId);
