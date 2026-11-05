using Malabak.Modules.Matches.Domain;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Matches.Application.Common;

public interface IMatchesDbContext
{
    DbSet<TeamChallenge> TeamChallenges { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
