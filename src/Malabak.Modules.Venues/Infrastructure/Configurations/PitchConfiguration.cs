using System.Text.Json;
using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Venues.Infrastructure.Configurations;

public sealed class PitchConfiguration : IEntityTypeConfiguration<Pitch>
{
    public void Configure(EntityTypeBuilder<Pitch> builder)
    {
        builder.ToTable("pitches");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.VenueId)
            .IsRequired();

        builder.Property(p => p.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.TurfType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.Capacity)
            .IsRequired();

        builder.Property(p => p.LengthMeters)
            .HasPrecision(6, 2);

        builder.Property(p => p.WidthMeters)
            .HasPrecision(6, 2);

        builder.Property(p => p.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(p => p.CreatedAtUtc)
            .IsRequired();

        builder.Property(p => p.UpdatedAtUtc);

        builder.Property(p => p.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.RowVersion).IsRowVersion();

        var stringListComparer = new ValueComparer<IReadOnlyCollection<string>>(
            (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList().AsReadOnly());

        builder.Property(p => p.Photos)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => (IReadOnlyCollection<string>)(JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()))
            .Metadata.SetValueComparer(stringListComparer);

        builder.HasIndex(p => new { p.VenueId, p.IsActive });
    }
}
