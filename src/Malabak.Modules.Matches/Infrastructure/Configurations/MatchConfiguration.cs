using Malabak.Modules.Matches.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Matches.Infrastructure.Configurations;

public sealed class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> builder)
    {
        builder.ToTable("matches");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.ChallengeId);

        builder.Property(m => m.HomeTeamId)
            .IsRequired();

        builder.Property(m => m.AwayTeamId)
            .IsRequired();

        builder.Property(m => m.PitchId);

        builder.Property(m => m.VenueId);

        builder.Property(m => m.VenueName)
            .HasMaxLength(200);

        builder.Property(m => m.ScheduledStartUtc)
            .IsRequired();

        builder.Property(m => m.ScheduledEndUtc)
            .IsRequired();

        builder.Property(m => m.DurationMinutes)
            .HasDefaultValue(Match.DefaultDurationMinutes)
            .IsRequired();

        builder.Property(m => m.BookingId);

        builder.Property(m => m.Status)
            .IsRequired();

        builder.Property(m => m.Outcome)
            .IsRequired();

        builder.Property(m => m.HomeScore);

        builder.Property(m => m.AwayScore);

        builder.Property(m => m.ResultStatus)
            .IsRequired();

        builder.Property(m => m.ResultSubmittedByTeamId);

        builder.Property(m => m.ResultSubmittedByPlayerId);

        builder.Property(m => m.ResultSubmittedAtUtc);

        builder.Property(m => m.ResultVerifiedByTeamId);

        builder.Property(m => m.ResultVerifiedByPlayerId);

        builder.Property(m => m.ResultVerifiedAtUtc);

        builder.Property(m => m.DisputeReason)
            .HasMaxLength(1000);

        builder.Property(m => m.DisputedAtUtc);

        builder.Property(m => m.CancelledByTeamId);

        builder.Property(m => m.CancellationReason)
            .HasMaxLength(500);

        builder.Property(m => m.Notes)
            .HasMaxLength(1000);

        builder.Property(m => m.AdminResolvedByUserId);

        builder.Property(m => m.AdminResolvedAtUtc);

        builder.Property(m => m.AdminResolutionNotes)
            .HasMaxLength(1000);

        builder.Property(m => m.AdminResolutionAction)
            .HasMaxLength(50);

        builder.Property(m => m.CreatedAtUtc)
            .IsRequired();

        builder.Property(m => m.UpdatedAtUtc);

        builder.Ignore(m => m.UncommittedAuditRecords);

        builder.HasIndex(m => m.HomeTeamId);
        builder.HasIndex(m => m.AwayTeamId);
        builder.HasIndex(m => m.ChallengeId);
        builder.HasIndex(m => m.BookingId);
        builder.HasIndex(m => m.ScheduledStartUtc);
        builder.HasIndex(m => m.ResultStatus);
        builder.HasIndex(m => new { m.Status, m.ScheduledStartUtc, m.ScheduledEndUtc });
    }
}
