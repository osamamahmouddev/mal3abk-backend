using Malabak.Modules.Matches.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Application.Matches.GetMatchAuditHistory;

public sealed record GetMatchAuditHistoryQuery(Guid MatchId) : IQuery<Result<IReadOnlyList<MatchResultAuditDto>>>;
