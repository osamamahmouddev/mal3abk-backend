using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.Members.LeaveTeam;

public sealed record LeaveTeamCommand(
    Guid TeamId,
    Guid PlayerId) : ICommand<Result>;
