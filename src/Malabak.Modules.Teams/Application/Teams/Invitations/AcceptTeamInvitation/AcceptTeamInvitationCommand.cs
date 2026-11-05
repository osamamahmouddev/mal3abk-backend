using Malabak.Modules.Teams.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.Invitations.AcceptTeamInvitation;

public sealed record AcceptTeamInvitationCommand(
    string InviteCode,
    Guid PlayerId) : ICommand<Result<TeamDetailDto>>;
