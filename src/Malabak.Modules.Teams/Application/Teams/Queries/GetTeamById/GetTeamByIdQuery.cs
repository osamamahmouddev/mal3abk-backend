using Malabak.Modules.Teams.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.Queries.GetTeamById;

public sealed record GetTeamByIdQuery(
    Guid TeamId,
    Guid? RequestingPlayerId = null) : IQuery<Result<TeamDetailDto>>;
