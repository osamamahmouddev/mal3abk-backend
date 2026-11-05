using Malabak.Modules.Teams.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.Queries.GetTeamProfile;

public sealed record GetTeamProfileQuery(
    Guid TeamId,
    Guid? RequestingPlayerId = null) : IQuery<Result<TeamProfileDto>>;
