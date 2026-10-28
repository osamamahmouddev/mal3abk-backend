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

        builder.Property(r => r.DayOfWeek);

        builder.Property(r => r.StartTime);

        builder.Property(r => r.EndTime);

        builder.OwnsOne(r => r.Price, money =>
        {
            money.Property(m => m.Amount)
                .HasPrecision(18, 2)
                .HasColumnName("price_amount")
                .IsRequired();

            money.Property(m => m.Currency)
                .HasMaxLength(3)
                .HasColumnName("price_currency")
                .IsRequired();
        });

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

        builder.HasIndex(r => new { r.VenueId, r.PitchId });
        builder.HasIndex(r => new { r.VenueId, r.DayOfWeek });
    }
}
