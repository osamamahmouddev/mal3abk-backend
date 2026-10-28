using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Venues.Infrastructure.Configurations;

public sealed class BlockedSlotConfiguration : IEntityTypeConfiguration<BlockedSlot>
{
    public void Configure(EntityTypeBuilder<BlockedSlot> builder)
    {
        builder.ToTable("blocked_slots");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.VenueId)
            .IsRequired();

        builder.Property(b => b.PitchId)
            .IsRequired();

        builder.Property(b => b.StartUtc)
            .IsRequired();

        builder.Property(b => b.EndUtc)
            .IsRequired();

        builder.Property(b => b.Reason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(b => b.BlockedByUserId)
            .IsRequired();

        builder.Property(b => b.CreatedAtUtc)
            .IsRequired();

        builder.Property(b => b.UpdatedAtUtc);

        builder.Property(b => b.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(b => new { b.PitchId, b.StartUtc, b.EndUtc });
        builder.HasIndex(b => b.VenueId);
    }
}
