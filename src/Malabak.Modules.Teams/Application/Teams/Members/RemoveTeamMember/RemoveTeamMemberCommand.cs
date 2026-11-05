using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.Members.RemoveTeamMember;

public sealed record RemoveTeamMemberCommand(
    Guid TeamId,
    Guid MemberPlayerId,
    Guid RequestingPlayerId) : ICommand<Result>;
