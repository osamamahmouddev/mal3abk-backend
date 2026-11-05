using Malabak.Modules.Teams.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Teams.Infrastructure.Configurations;

public sealed class TeamFairPlayReportConfiguration : IEntityTypeConfiguration<TeamFairPlayReport>
{
    public void Configure(EntityTypeBuilder<TeamFairPlayReport> builder)
    {
        builder.ToTable("fair_play_reports");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ReporterTeamId)
            .IsRequired();

        builder.Property(r => r.ReporterPlayerId)
            .IsRequired();

        builder.Property(r => r.ReportedTeamId)
            .IsRequired();

        builder.Property(r => r.MatchId);

        builder.Property(r => r.Category)
            .IsRequired();

        builder.Property(r => r.Description)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(r => r.EvidenceUrls)
            .HasMaxLength(2000);

        builder.Property(r => r.Status)
            .IsRequired();

        builder.Property(r => r.PenaltyType)
            .IsRequired();

        builder.Property(r => r.PointsDeducted);

        builder.Property(r => r.AdminDecisionNotes)
            .HasMaxLength(1000);

        builder.Property(r => r.AdminReviewedByUserId);

        builder.Property(r => r.AdminReviewedAtUtc);

        builder.Property(r => r.AppealReason)
            .HasMaxLength(1000);

        builder.Property(r => r.AppealedByPlayerId);

        builder.Property(r => r.AppealedAtUtc);

        builder.Property(r => r.AdminAppealNotes)
            .HasMaxLength(1000);

        builder.Property(r => r.AdminAppealResolvedByUserId);

        builder.Property(r => r.AdminAppealResolvedAtUtc);

        builder.Property(r => r.CreatedAtUtc)
            .IsRequired();

        builder.Property(r => r.UpdatedAtUtc);

        builder.HasIndex(r => r.ReportedTeamId);
        builder.HasIndex(r => r.ReporterTeamId);
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.CreatedAtUtc);
    }
}
