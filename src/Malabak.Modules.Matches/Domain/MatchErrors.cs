using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Domain;

public static class MatchErrors
{
    public static readonly Error NotFound =
        Error.NotFound("Match.NotFound", "The requested match was not found.");

    public static readonly Error SameTeam =
        Error.Validation("Match.SameTeam", "A team cannot schedule a match against itself.");

    public static readonly Error ScheduleConflict =
        Error.Conflict("Match.ScheduleConflict", "One or both teams have a conflicting match scheduled during this time window.");

    public static readonly Error InvalidDuration =
        Error.Validation("Match.InvalidDuration", "Match duration must be between 30 and 240 minutes.");

    public static readonly Error InvalidScheduledTime =
        Error.Validation("Match.InvalidScheduledTime", "Scheduled start time cannot be in the past.");

    public static readonly Error NotAuthorizedCaptain =
        Error.Forbidden("Match.NotAuthorizedCaptain", "Only the captain or vice-captain of a participating team can manage the match.");

    public static readonly Error InvalidStatus =
        Error.Validation("Match.InvalidStatus", "The match is not in a valid state for this action.");

    public static readonly Error AlreadyLinkedToBooking =
        Error.Conflict("Match.AlreadyLinkedToBooking", "The match is already linked to a pitch booking.");

    public static readonly Error BookingMismatch =
        Error.Validation("Match.BookingMismatch", "The provided booking identifier is invalid.");
}
