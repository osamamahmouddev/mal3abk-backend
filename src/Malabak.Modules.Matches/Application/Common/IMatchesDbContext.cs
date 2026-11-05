using Malabak.Modules.Matches.Domain;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Matches.Application.Common;

public interface IMatchesDbContext
{
    DbSet<TeamChallenge> TeamChallenges { get; }
    DbSet<Match> Matches { get; }
    DbSet<MatchResultAuditRecord> MatchResultAuditRecords { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
