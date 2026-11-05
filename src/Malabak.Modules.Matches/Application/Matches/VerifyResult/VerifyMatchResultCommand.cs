using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Application.Matches.VerifyResult;

public sealed record VerifyMatchResultCommand(
    Guid MatchId,
    Guid VerifyingTeamId,
    MatchVerificationAction Action,
    string? DisputeReason,
    Guid CallerPlayerId) : ICommand<Result<MatchDto>>;
