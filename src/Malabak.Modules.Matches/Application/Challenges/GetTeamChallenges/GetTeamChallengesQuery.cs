using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Application.Challenges.GetTeamChallenges;

public sealed record GetTeamChallengesQuery(
    Guid TeamId,
    Guid CallerPlayerId,
    ChallengeStatus? Status = null) : IQuery<Result<IReadOnlyList<TeamChallengeDto>>>;
