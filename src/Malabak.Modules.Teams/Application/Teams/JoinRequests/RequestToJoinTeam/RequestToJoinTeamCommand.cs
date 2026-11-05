using Malabak.Modules.Teams.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.JoinRequests.RequestToJoinTeam;

public sealed record RequestToJoinTeamCommand(
    Guid TeamId,
    Guid PlayerId,
    string? Message) : ICommand<Result<TeamJoinRequestDto>>;
