using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Application.Matches.ResolveDispute;

public sealed record ResolveMatchDisputeCommand(
    Guid MatchId,
    Guid AdminUserId,
    MatchDisputeResolutionAction Action,
    string ResolutionNotes,
    int? OfficialHomeScore = null,
    int? OfficialAwayScore = null) : ICommand<Result<MatchDto>>;
