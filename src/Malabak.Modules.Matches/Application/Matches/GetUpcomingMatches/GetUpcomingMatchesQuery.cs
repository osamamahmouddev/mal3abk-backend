using Malabak.Modules.Matches.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Application.Matches.GetUpcomingMatches;

public sealed record GetUpcomingMatchesQuery(
    Guid? TeamId = null,
    int Page = 1,
    int PageSize = 20) : IQuery<Result<PagedList<MatchDto>>>;
