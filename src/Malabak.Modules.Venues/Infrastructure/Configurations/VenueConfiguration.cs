using System.Text.Json;
using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Venues.Infrastructure.Configurations;

public sealed class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        builder.ToTable("venues");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(v => v.Slug)
            .HasMaxLength(200)
            .IsRequired();

        builder.HasIndex(v => v.Slug)
            .IsUnique();

        builder.Property(v => v.Description)
            .HasMaxLength(2000);

        builder.Property(v => v.OwnerId)
            .IsRequired();

        builder.HasIndex(v => v.OwnerId);

        builder.Property(v => v.Address)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(v => v.City)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(v => v.Area)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(v => new { v.City, v.Area });

        // Explore and map search: bounding-box range scans on searchable venues (no PostGIS; Haversine runs in memory).
        builder.HasIndex(v => new { v.Latitude, v.Longitude })
            .HasDatabaseName("ix_venues_searchable_location")
            .HasFilter("\"IsPublished\" = TRUE AND \"IsDeleted\" = FALSE AND \"Latitude\" IS NOT NULL");

        builder.Property(v => v.Latitude);
        builder.Property(v => v.Longitude);

        builder.Property(v => v.PhoneNumber)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(v => v.IsApproved)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(v => v.IsPublished)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(v => v.ApprovalStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(VenueApprovalStatus.Draft);

        builder.Property(v => v.PricingModel)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(PricingModel.Subscription);

        builder.Property(v => v.CustomCommissionEnabled)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(v => v.CommissionType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(CommissionType.PlatformDefault);

        builder.Property(v => v.CommissionRate)
            .HasPrecision(18, 2);

        builder.Ignore(v => v.IsSearchable);
        builder.Property(v => v.AdminNote).HasMaxLength(Venue.MaxAdminNoteLength);
        builder.Property(v => v.PublishedBeforeSuspension).IsRequired().HasDefaultValue(false);

        builder.HasIndex(v => new { v.IsApproved, v.IsPublished });
        builder.HasIndex(v => v.ApprovalStatus);

        builder.Property(v => v.CreatedAtUtc)
            .IsRequired();

        builder.Property(v => v.UpdatedAtUtc);

        builder.Property(v => v.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.RowVersion).IsRowVersion();

        var stringListComparer = new ValueComparer<IReadOnlyCollection<string>>(
            (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList()); // List snapshot: matches the List backing field when EF re-materializes it

        builder.Property(v => v.Facilities)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => (IReadOnlyCollection<string>)(JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()))
            .Metadata.SetValueComparer(stringListComparer);

        builder.Property(v => v.Photos)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => (IReadOnlyCollection<string>)(JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()))
            .Metadata.SetValueComparer(stringListComparer);

        builder.HasMany(v => v.Pitches)
            .WithOne()
            .HasForeignKey(p => p.VenueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(v => v.OperatingSchedules)
            .WithOne()
            .HasForeignKey(s => s.VenueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(v => v.PriceRules)
            .WithOne()
            .HasForeignKey(r => r.VenueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(v => v.BlockedSlots)
            .WithOne()
            .HasForeignKey(b => b.VenueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(v => v.Policies)
            .WithOne()
            .HasForeignKey(p => p.VenueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(v => v.PaymentMethods)
            .WithOne()
            .HasForeignKey(m => m.VenueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
