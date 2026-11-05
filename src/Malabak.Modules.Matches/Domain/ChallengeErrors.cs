using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Domain;

public static class ChallengeErrors
{
    public static readonly Error NotFound = Error.NotFound("Challenge.NotFound", "The challenge was not found.");
    public static readonly Error CannotChallengeSelf = Error.Validation("Challenge.CannotChallengeSelf", "A team cannot challenge itself.");
    public static readonly Error TargetNotOpenForChallenges = Error.Conflict("Challenge.TargetNotOpenForChallenges", "The challenged team is currently not accepting match challenges.");
    public static readonly Error DateInPast = Error.Validation("Challenge.DateInPast", "The proposed match date must be in the future.");
    public static readonly Error InvalidDuration = Error.Validation("Challenge.InvalidDuration", "Match duration must be between 30 and 180 minutes.");
    public static readonly Error NotYourTurn = Error.Forbidden("Challenge.NotYourTurn", "It is not your team's turn to respond to this challenge.");
    public static readonly Error InvalidStatus = Error.Conflict("Challenge.InvalidStatus", "This action cannot be performed on the challenge in its current status.");
    public static readonly Error NotAuthorizedCaptain = Error.Forbidden("Challenge.NotAuthorizedCaptain", "Only the captain or vice-captain of the participating team can perform this action.");
    public static readonly Error ChallengeExpired = Error.Conflict("Challenge.Expired", "The proposed match date has already passed or the challenge has expired.");
}
