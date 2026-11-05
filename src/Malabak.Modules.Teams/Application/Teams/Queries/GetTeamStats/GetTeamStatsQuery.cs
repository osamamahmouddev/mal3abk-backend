using Malabak.Modules.Teams.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.Queries.GetTeamStats;

public sealed record GetTeamStatsQuery(Guid TeamId) : IQuery<Result<TeamStatsDto>>;
