using Malabak.Modules.Teams.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.Queries.DiscoverTeams;

public sealed record DiscoverTeamsQuery(
    string? Search = null,
    string? City = null,
    string? Governorate = null,
    string? District = null,
    double? MinRating = null,
    double? MaxRating = null,
    bool? IsRecruiting = null,
    bool? IsOpenForChallenges = null,
    string? SortBy = null,
    int Page = 1,
    int PageSize = 20,
    Guid? RequestingPlayerId = null) : IQuery<Result<PagedList<TeamDiscoveryDto>>>;
