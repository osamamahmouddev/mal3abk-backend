using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.Members.UpdateMemberRole;

/// <summary>The captain appoints a vice-captain or sets a vice-captain back to member.</summary>
public sealed record UpdateMemberRoleCommand(
    Guid TeamId,
    Guid TargetPlayerId,
    TeamRole NewRole,
    Guid RequestingPlayerId) : ICommand<Result>;
