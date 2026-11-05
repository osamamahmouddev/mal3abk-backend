using Malabak.Modules.Matches.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Matches.Infrastructure.Configurations;

public sealed class MatchResultAuditRecordConfiguration : IEntityTypeConfiguration<MatchResultAuditRecord>
{
    public void Configure(EntityTypeBuilder<MatchResultAuditRecord> builder)
    {
        builder.ToTable("match_result_audit_records");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.MatchId)
            .IsRequired();

        builder.Property(a => a.PreviousHomeScore);

        builder.Property(a => a.PreviousAwayScore);

        builder.Property(a => a.NewHomeScore);

        builder.Property(a => a.NewAwayScore);

        builder.Property(a => a.PreviousStatus)
            .IsRequired();

        builder.Property(a => a.NewStatus)
            .IsRequired();

        builder.Property(a => a.ChangedByUserId)
            .IsRequired();

        builder.Property(a => a.ChangeType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.Reason)
            .HasMaxLength(1000);

        builder.Property(a => a.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(a => a.MatchId);
        builder.HasIndex(a => a.CreatedAtUtc);
    }
}
