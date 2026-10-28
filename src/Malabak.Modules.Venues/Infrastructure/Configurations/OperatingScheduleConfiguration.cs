using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Venues.Infrastructure.Configurations;

public sealed class OperatingScheduleConfiguration : IEntityTypeConfiguration<OperatingSchedule>
{
    public void Configure(EntityTypeBuilder<OperatingSchedule> builder)
    {
        builder.ToTable("operating_schedules");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.VenueId)
            .IsRequired();

        builder.Property(s => s.DayOfWeek)
            .IsRequired();

        builder.Property(s => s.OpenTime)
            .IsRequired();

        builder.Property(s => s.CloseTime)
            .IsRequired();

        builder.Property(s => s.SlotDurationMinutes)
            .IsRequired()
            .HasDefaultValue(60);

        builder.Property(s => s.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(s => s.CreatedAtUtc)
            .IsRequired();

        builder.Property(s => s.UpdatedAtUtc);

        builder.Property(s => s.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(s => new { s.VenueId, s.DayOfWeek });
    }
}
