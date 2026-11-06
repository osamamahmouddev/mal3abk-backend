using Malabak.Modules.Teams.Domain;
using Malabak.Modules.Teams.Infrastructure;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Admin;

/// <summary>
/// A player's community history for the admin user page (TASK-701): team memberships, fair-play reports they filed and
/// reports against the teams they captain (the "reported user" signal).
/// </summary>
public sealed class TeamUserActivityProvider : IUserActivityProvider
{
    private readonly TeamsDbContext _dbContext;

    public TeamUserActivityProvider(TeamsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Source => "community";

    public async Task<UserActivitySummary> GetAsync(Guid userId, int take, CancellationToken cancellationToken = default)
    {
        var memberships = await (
                from m in _dbContext.TeamMembers.AsNoTracking()
                join t in _dbContext.Teams.AsNoTracking() on m.TeamId equals t.Id
                where m.PlayerId == userId
                orderby m.JoinedAtUtc descending
                select new { t.Id, t.Name, m.Role, m.IsActive, m.JoinedAtUtc })
            .Take(take).ToListAsync(cancellationToken);

        var captainOf = memberships.Where(m => m.IsActive && m.Role == TeamRole.Captain).Select(m => m.Id).ToList();
        var filed = await _dbContext.FairPlayReports.AsNoTracking()
            .Where(r => r.ReporterPlayerId == userId).OrderByDescending(r => r.CreatedAtUtc).Take(take)
            .Select(r => new { r.Id, r.Category, r.Status, r.CreatedAtUtc }).ToListAsync(cancellationToken);
        var against = captainOf.Count == 0
            ? []
            : await _dbContext.FairPlayReports.AsNoTracking()
                .Where(r => captainOf.Contains(r.ReportedTeamId)).OrderByDescending(r => r.CreatedAtUtc).Take(take)
                .Select(r => new { r.Id, r.Category, r.Status, r.CreatedAtUtc }).ToListAsync(cancellationToken);

        var items = memberships.Select(m => new UserActivityItem("team_membership", m.Name, m.IsActive ? m.Role.ToString() : "Left", m.JoinedAtUtc, m.Id.ToString("D")))
            .Concat(filed.Select(r => new UserActivityItem("report_filed", r.Category.ToString(), r.Status.ToString(), r.CreatedAtUtc, r.Id.ToString("D"))))
            .Concat(against.Select(r => new UserActivityItem("report_against_team", r.Category.ToString(), r.Status.ToString(), r.CreatedAtUtc, r.Id.ToString("D"))))
            .OrderByDescending(i => i.OccurredAtUtc).Take(take).ToList();

        var counts = new Dictionary<string, int>
        {
            ["teams"] = memberships.Count(m => m.IsActive),
            ["reportsFiled"] = filed.Count,
            ["reportsAgainstTeams"] = against.Count
        };
        return new UserActivitySummary(Source, items.Count, counts, items);
    }
}
