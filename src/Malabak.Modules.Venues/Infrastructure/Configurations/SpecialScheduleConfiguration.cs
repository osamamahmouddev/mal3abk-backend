using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Venues.Infrastructure.Configurations;

public sealed class ScheduleOverrideConfiguration : IEntityTypeConfiguration<ScheduleOverride>
{
    public void Configure(EntityTypeBuilder<ScheduleOverride> builder)
    {
        builder.ToTable("schedule_overrides");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Reason).HasMaxLength(ScheduleOverride.MaxReasonLength).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(e => new { e.VenueId, e.StartDate, e.EndDate }).HasFilter("\"IsDeleted\" = FALSE");
    }
}

public sealed class PitchWeeklyHoursConfiguration : IEntityTypeConfiguration<PitchWeeklyHours>
{
    public void Configure(EntityTypeBuilder<PitchWeeklyHours> builder)
    {
        builder.ToTable("pitch_weekly_hours");
        builder.HasKey(h => h.Id);
        builder.HasIndex(h => new { h.PitchId, h.DayOfWeek }).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(h => h.VenueId);
        builder.Property(x => x.RowVersion).IsRowVersion();
    }
}

public sealed class BookingRulesConfiguration : IEntityTypeConfiguration<BookingRules>
{
    public void Configure(EntityTypeBuilder<BookingRules> builder)
    {
        builder.ToTable("booking_rules");
        builder.HasKey(r => r.Id);
        builder.Property(x => x.RowVersion).IsRowVersion();

        // One venue default (PitchId null) and at most one override per pitch.
        builder.HasIndex(r => new { r.VenueId, r.PitchId }).IsUnique().AreNullsDistinct(false);
    }
}
