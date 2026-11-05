using Malabak.Modules.Teams.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.Invitations.CreateTeamInvitation;

public sealed record CreateTeamInvitationCommand(
    Guid TeamId,
    Guid RequestingPlayerId,
    int? ValidityDays = 7,
    Guid? InvitedPlayerId = null) : ICommand<Result<TeamInvitationDto>>;
