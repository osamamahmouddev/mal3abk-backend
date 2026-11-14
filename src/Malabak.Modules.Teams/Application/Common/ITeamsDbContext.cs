using Malabak.Modules.Teams.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Malabak.Modules.Teams.Application.Common;

public interface ITeamsDbContext
{
    DbSet<Team> Teams { get; }
    DbSet<TeamMember> TeamMembers { get; }
    DbSet<TeamJoinRequest> TeamJoinRequests { get; }
    DbSet<TeamFairPlayReport> FairPlayReports { get; }
    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
