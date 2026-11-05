using Malabak.Modules.Matches.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Application.Matches.GetDisputedMatches;

public sealed record GetDisputedMatchesQuery(
    int Page = 1,
    int PageSize = 20) : IQuery<Result<PagedList<DisputedMatchDto>>>;
