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
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchResultAuditRecord> MatchResultAuditRecords => Set<MatchResultAuditRecord>();

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

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var matchesWithAudits = ChangeTracker.Entries<Match>()
            .Select(entry => entry.Entity)
            .Where(m => m.UncommittedAuditRecords.Count > 0)
            .ToList();

        foreach (var match in matchesWithAudits)
        {
            MatchResultAuditRecords.AddRange(match.UncommittedAuditRecords);
            match.ClearUncommittedAuditRecords();
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
