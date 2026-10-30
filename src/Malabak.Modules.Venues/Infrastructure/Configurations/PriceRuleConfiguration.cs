using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Venues.Infrastructure.Configurations;

public sealed class PriceRuleConfiguration : IEntityTypeConfiguration<PriceRule>
{
    public void Configure(EntityTypeBuilder<PriceRule> builder)
    {
        builder.ToTable("price_rules");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.VenueId)
            .IsRequired();

        builder.Property(r => r.PitchId);

        builder.Property(r => r.Kind)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.Adjustment)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.Name)
            .HasMaxLength(PriceRule.MaxNameLength)
            .IsRequired();

        builder.Property(r => r.Amount)
            .HasPrecision(18, 2)
            .HasColumnName("price_amount");

        builder.Property(r => r.Currency)
            .HasMaxLength(3)
            .HasColumnName("price_currency")
            .IsRequired();

        builder.Ignore(r => r.Price);

        builder.Property(r => r.Percent)
            .HasPrecision(9, 2);

        builder.Property(r => r.Days)
            .HasConversion<int?>();

        builder.Property(r => r.StartTime);

        builder.Property(r => r.EndTime);

        builder.Property(r => r.StartDate);

        builder.Property(r => r.EndDate);

        builder.Property(r => r.MinDurationMinutes);

        builder.Property(r => r.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(r => r.CreatedAtUtc)
            .IsRequired();

        builder.Property(r => r.UpdatedAtUtc);

        builder.Property(r => r.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(r => new { r.VenueId, r.Kind });
        builder.HasIndex(r => new { r.VenueId, r.PitchId });
    }
}
