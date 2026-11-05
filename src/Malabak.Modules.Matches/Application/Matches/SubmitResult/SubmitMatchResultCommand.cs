using Malabak.Modules.Matches.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Application.Matches.SubmitResult;

public sealed record SubmitMatchResultCommand(
    Guid MatchId,
    Guid SubmittingTeamId,
    int HomeScore,
    int AwayScore,
    Guid CallerPlayerId) : ICommand<Result<MatchDto>>;
