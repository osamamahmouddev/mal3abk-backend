using Malabak.Modules.Teams.Domain;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Common;

public interface ITeamsDbContext
{
    DbSet<Team> Teams { get; }
    DbSet<TeamMember> TeamMembers { get; }
    DbSet<TeamJoinRequest> TeamJoinRequests { get; }
    DbSet<TeamFairPlayReport> FairPlayReports { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
