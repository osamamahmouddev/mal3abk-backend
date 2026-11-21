using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.Invitations.PreviewTeamInvitation;

/// <summary>
/// Resolves an invite code (from a shared link or typed) to its team so the player can see where it leads and confirm.
/// A malformed, unknown, rotated or suspended team's code all answer <see cref="TeamErrors.InvalidInviteCode"/>, so the
/// answer never tells codes apart; an expired one says so, as joining would.
/// </summary>
public sealed class PreviewTeamInvitationHandler : IQueryHandler<PreviewTeamInvitationQuery, Result<TeamInvitePreviewDto>>
{
    private readonly ITeamsDbContext _dbContext;
    private readonly IClock _clock;

    public PreviewTeamInvitationHandler(ITeamsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<TeamInvitePreviewDto>> HandleAsync(PreviewTeamInvitationQuery query, CancellationToken cancellationToken = default)
    {
        var code = TeamInviteCodes.Normalize(query.InviteCode);
        if (code is null)
        {
            return Result.Failure<TeamInvitePreviewDto>(TeamErrors.InvalidInviteCode);
        }

        var team = await _dbContext.Teams
            .AsNoTracking()
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.InviteCode == code, cancellationToken);
        if (team is null || team.IsSuspended)
        {
            return Result.Failure<TeamInvitePreviewDto>(TeamErrors.InvalidInviteCode);
        }

        if (team.InviteCodeExpiresAtUtc is { } expires && expires < _clock.UtcNow)
        {
            return Result.Failure<TeamInvitePreviewDto>(TeamErrors.InviteCodeExpired);
        }

        var active = team.Members.Where(m => m.IsActive).ToList();
        return Result.Success(new TeamInvitePreviewDto(
            team.Id, team.Name, team.LogoUrl, team.City, team.Governorate, team.District,
            active.Count, Team.MaxTeamMembers, team.InviteCodeExpiresAtUtc,
            active.Exists(m => m.PlayerId == query.PlayerId)));
    }
}
