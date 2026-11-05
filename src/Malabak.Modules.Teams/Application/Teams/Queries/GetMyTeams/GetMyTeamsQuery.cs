using Malabak.Modules.Teams.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.Queries.GetMyTeams;

public sealed record GetMyTeamsQuery(Guid PlayerId) : IQuery<Result<IReadOnlyList<TeamSummaryDto>>>;
