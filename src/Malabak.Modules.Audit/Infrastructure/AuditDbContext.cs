using Malabak.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Audit.Infrastructure;

public sealed class AuditDbContext : DbContext
{
    public const string Schema = AuditModule.Schema;

    public DbSet<AuditLogEntry> Entries => Set<AuditLogEntry>();

    public AuditDbContext(DbContextOptions<AuditDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AuditLogEntry>(b =>
        {
            b.ToTable("audit_logs");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.ActorType).HasMaxLength(20).IsRequired();
            b.Property(e => e.Action).HasMaxLength(100).IsRequired();
            b.Property(e => e.EntityType).HasMaxLength(100).IsRequired();
            b.Property(e => e.EntityId).HasMaxLength(100).IsRequired();
            b.Property(e => e.Reason).HasMaxLength(1000);
            b.Property(e => e.IpAddress).HasMaxLength(64);
            b.Property(e => e.CorrelationId).HasMaxLength(100);
            b.HasIndex(e => e.OccurredAtUtc);
            b.HasIndex(e => new { e.EntityType, e.EntityId, e.OccurredAtUtc });
            b.HasIndex(e => new { e.ActorId, e.OccurredAtUtc });
            b.HasIndex(e => new { e.Action, e.OccurredAtUtc });
        });
    }
}
