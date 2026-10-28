using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Venues.Infrastructure.Configurations;

public sealed class VenuePolicyConfiguration : IEntityTypeConfiguration<VenuePolicy>
{
    public void Configure(EntityTypeBuilder<VenuePolicy> builder)
    {
        builder.ToTable("policies");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.VenueId).IsRequired();
        builder.Property(p => p.Version).IsRequired();
        builder.Property(p => p.IsCurrent).IsRequired();
        builder.Property(p => p.EffectiveFromUtc).IsRequired();
        builder.Property(p => p.CreatedByUserId).IsRequired();
        builder.Property(p => p.DepositType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.DepositValue).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(p => p.RefundEligibility).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.RefundPercentage).HasColumnType("numeric(5,2)").IsRequired();
        builder.Property(p => p.NoShowRule).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(p => p.Terms).HasMaxLength(BookingPolicyTerms.MaxTermsLength);
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.RowVersion).IsRowVersion();

        // Versions are unique per venue, and at most one version is current (prevents two concurrent edits both "winning").
        builder.HasIndex(p => new { p.VenueId, p.Version }).IsUnique();
        builder.HasIndex(p => p.VenueId)
            .IsUnique()
            .HasFilter("\"IsCurrent\" = TRUE")
            .HasDatabaseName("ux_policies_single_current");
    }
}
