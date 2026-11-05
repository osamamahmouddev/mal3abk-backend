using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.JoinRequests.GetTeamJoinRequests;

public sealed record GetTeamJoinRequestsQuery(
    Guid TeamId,
    Guid CallerPlayerId,
    JoinRequestStatus? Status = null) : IQuery<Result<IReadOnlyList<TeamJoinRequestDto>>>;
