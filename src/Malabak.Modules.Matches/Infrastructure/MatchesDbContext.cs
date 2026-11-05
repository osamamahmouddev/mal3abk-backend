using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Matches.Infrastructure;

public sealed class MatchesDbContext : BaseDbContext, IMatchesDbContext
{
    public const string Schema = "matches";
    public override string ModuleSchema => Schema;

    public DbSet<TeamChallenge> TeamChallenges => Set<TeamChallenge>();

    public MatchesDbContext(
        DbContextOptions<MatchesDbContext> options,
        IClock? clock = null)
        : base(options, clock)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MatchesDbContext).Assembly);
    }
}
