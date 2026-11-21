using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Application.Teams.Invitations.PreviewTeamInvitation;

/// <summary>What an invite code leads to, shown to the player before they choose to join.</summary>
public sealed record PreviewTeamInvitationQuery(string InviteCode, Guid PlayerId) : IQuery<Result<TeamInvitePreviewDto>>;

/// <summary>
/// Public facts of the team behind a valid, unexpired invite code. Never the code itself, the roster or anything only
/// members see: knowing a code is not membership.
/// </summary>
public sealed record TeamInvitePreviewDto(
    Guid TeamId,
    string Name,
    string? LogoUrl,
    string City,
    string Governorate,
    string? District,
    int MemberCount,
    int MaxMembers,
    DateTime? ExpiresAtUtc,
    bool IsAlreadyMember);
