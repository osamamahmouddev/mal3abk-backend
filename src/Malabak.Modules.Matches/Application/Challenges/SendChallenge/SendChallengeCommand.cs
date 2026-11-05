using Malabak.Modules.Matches.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Application.Challenges.SendChallenge;

public sealed record SendChallengeCommand(
    Guid ChallengerTeamId,
    Guid ChallengedTeamId,
    Guid CallerPlayerId,
    DateTime ProposedDateUtc,
    int DurationMinutes,
    Guid? ProposedPitchId,
    Guid? ProposedVenueId,
    string? ProposedVenueName,
    string? Notes) : ICommand<Result<TeamChallengeDto>>;
