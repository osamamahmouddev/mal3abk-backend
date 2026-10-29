using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Venues.Infrastructure.Configurations;

public sealed class FavoriteVenueConfiguration : IEntityTypeConfiguration<FavoriteVenue>
{
    public void Configure(EntityTypeBuilder<FavoriteVenue> builder)
    {
        builder.ToTable("favorite_venues");
        builder.HasKey(f => new { f.UserId, f.VenueId });
        builder.HasIndex(f => new { f.UserId, f.CreatedAtUtc });
    }
}

public sealed class RecentlyViewedVenueConfiguration : IEntityTypeConfiguration<RecentlyViewedVenue>
{
    public void Configure(EntityTypeBuilder<RecentlyViewedVenue> builder)
    {
        builder.ToTable("recently_viewed_venues");
        builder.HasKey(r => new { r.UserId, r.VenueId });
        builder.HasIndex(r => new { r.UserId, r.ViewedAtUtc });
    }
}
