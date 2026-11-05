using Malabak.Modules.Matches.Domain;

namespace Malabak.Modules.Matches.Application.Common;

public static class ChallengeMapper
{
    public static TeamChallengeDto ToDto(TeamChallenge challenge, string? challengerName, string? challengedName, Guid? viewingTeamId)
    {
        return new TeamChallengeDto(
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
            viewingTeamId.HasValue && challenge.CurrentTurnTeamId == viewingTeamId.Value,
            challenge.AcceptedAtUtc,
            challenge.DeclinedAtUtc,
            challenge.DeclinedReason,
            challenge.MatchId,
            challenge.BookingId,
            challenge.Status == ChallengeStatus.Accepted ? challenge.GetBookingDeepLink() : null,
            challenge.CreatedAtUtc);
    }
}
