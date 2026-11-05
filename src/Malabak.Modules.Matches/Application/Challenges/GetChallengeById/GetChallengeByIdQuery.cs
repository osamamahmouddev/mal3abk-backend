using Malabak.Modules.Matches.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Application.Challenges.GetChallengeById;

public sealed record GetChallengeByIdQuery(
    Guid ChallengeId,
    Guid CallerPlayerId) : IQuery<Result<TeamChallengeDto>>;
