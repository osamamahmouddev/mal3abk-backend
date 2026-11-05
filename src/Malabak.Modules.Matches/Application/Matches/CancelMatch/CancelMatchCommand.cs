using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Application.Matches.CancelMatch;

public sealed record CancelMatchCommand(
    Guid MatchId,
    Guid CallerTeamId,
    string? Reason,
    Guid CallerPlayerId) : ICommand<Result>;
