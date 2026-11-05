using Malabak.Modules.Teams.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.Queries.GetTeamLeaderboard;

public sealed record GetTeamLeaderboardQuery(
    string? City = null,
    string? Governorate = null,
    int Page = 1,
    int PageSize = 20) : IQuery<Result<PagedList<TeamLeaderboardEntryDto>>>;
