using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Venues.Infrastructure.Configurations;

public sealed class VenuePaymentMethodConfiguration : IEntityTypeConfiguration<VenuePaymentMethod>
{
    public void Configure(EntityTypeBuilder<VenuePaymentMethod> builder)
    {
        builder.ToTable("payment_methods");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.VenueId)
            .IsRequired();

        builder.Property(m => m.MethodType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(m => m.AccountIdentifier)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(m => m.AccountName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(m => m.Instructions)
            .HasMaxLength(1000);

        builder.Property(m => m.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(m => m.CreatedAtUtc)
            .IsRequired();

        builder.Property(m => m.UpdatedAtUtc);

        builder.Property(m => m.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(m => new { m.VenueId, m.IsActive });
    }
}
