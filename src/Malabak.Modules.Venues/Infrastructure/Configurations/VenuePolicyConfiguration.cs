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

        builder.Property(p => p.VenueId)
            .IsRequired();

        builder.Property(p => p.DepositPercentage)
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(p => p.CancellationWindowHours)
            .IsRequired();

        builder.Property(p => p.RefundPolicyType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.TermsAndConditions)
            .HasMaxLength(4000);

        builder.Property(p => p.CreatedAtUtc)
            .IsRequired();

        builder.Property(p => p.UpdatedAtUtc);

        builder.Property(p => p.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.VenueId);
    }
}
