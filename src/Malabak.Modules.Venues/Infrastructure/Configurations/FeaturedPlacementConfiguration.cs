using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Venues.Infrastructure.Configurations;

public sealed class FeaturedPlacementConfiguration : IEntityTypeConfiguration<FeaturedPlacement>
{
    public void Configure(EntityTypeBuilder<FeaturedPlacement> builder)
    {
        builder.ToTable("featured_placements");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Slot).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.Title).HasMaxLength(FeaturedPlacement.MaxTitleLength);
        builder.HasIndex(p => new { p.Slot, p.StartsAtUtc, p.EndsAtUtc });
        builder.HasIndex(p => p.VenueId);
    }
}
