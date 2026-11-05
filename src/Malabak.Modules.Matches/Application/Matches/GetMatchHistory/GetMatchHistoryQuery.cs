using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Application.Matches.GetMatchHistory;

public sealed record GetMatchHistoryQuery(
    Guid? TeamId = null,
    MatchStatus? Status = null,
    int Page = 1,
    int PageSize = 20) : IQuery<Result<PagedList<MatchDto>>>;
