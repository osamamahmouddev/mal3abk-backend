using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Venues.Infrastructure.Configurations;

public sealed class VenuePaymentMethodConfiguration : IEntityTypeConfiguration<VenuePaymentMethod>
{
    public void Configure(EntityTypeBuilder<VenuePaymentMethod> builder)
    {
        builder.ToTable(VenuePaymentMethod.TableName);

        builder.HasKey(m => m.Id);

        builder.Property(m => m.VenueId)
            .IsRequired();

        builder.Property(m => m.MethodType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(m => m.RecipientName)
            .HasMaxLength(VenuePaymentMethod.MaxRecipientNameLength)
            .IsRequired();

        builder.Property(m => m.AccountDetailsCiphertext)
            .IsRequired();

        builder.Property(m => m.AccountDetailsMask)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(m => m.Instructions)
            .HasMaxLength(VenuePaymentMethod.MaxInstructionsLength);

        builder.Property(m => m.DepositType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.DepositValue)
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Ignore(m => m.DepositRule);

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
