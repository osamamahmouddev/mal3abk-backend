using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Common;

/// <summary>Lifts the "removed by a leader" block on a player's earlier, ended memberships of a team.</summary>
internal static class TeamRejoin
{
    public static async Task AllowAsync(ITeamsDbContext db, Guid teamId, Guid playerId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        // Ended memberships are soft-deleted, so they are read past the filter.
        var blocked = await db.TeamMembers
            .IgnoreQueryFilters()
            .Where(m => m.TeamId == teamId && m.PlayerId == playerId && m.RejoinBlocked)
            .ToListAsync(cancellationToken);

        foreach (var membership in blocked)
        {
            membership.AllowRejoin(nowUtc);
        }
    }
}
