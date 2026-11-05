using Malabak.Modules.Matches.Domain;

namespace Malabak.Modules.Matches.Application.Common;

public static class MatchMapper
{
    public static MatchDto ToDto(Match match, string homeTeamName, string awayTeamName)
    {
        return new MatchDto(
            match.Id,
            match.ChallengeId,
            match.HomeTeamId,
            homeTeamName,
            match.AwayTeamId,
            awayTeamName,
            match.PitchId,
            match.VenueId,
            match.VenueName,
            match.ScheduledStartUtc,
            match.ScheduledEndUtc,
            match.DurationMinutes,
            match.BookingId,
            match.Status.ToString(),
            match.Outcome.ToString(),
            match.HomeScore,
            match.AwayScore,
            match.GetBookingDeepLink(),
            match.Notes,
            match.CreatedAtUtc);
    }
}
