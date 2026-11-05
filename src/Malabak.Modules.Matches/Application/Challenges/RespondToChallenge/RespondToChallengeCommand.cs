using Malabak.Modules.Matches.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Application.Challenges.RespondToChallenge;

public sealed record RespondToChallengeCommand(
    Guid ChallengeId,
    Guid RespondingTeamId,
    Guid CallerPlayerId,
    ChallengeResponseAction Action,
    DateTime? NewProposedDateUtc = null,
    int? NewDurationMinutes = null,
    Guid? NewProposedPitchId = null,
    Guid? NewProposedVenueId = null,
    string? NewProposedVenueName = null,
    string? Notes = null,
    string? DeclineReason = null) : ICommand<Result<TeamChallengeDto>>;
