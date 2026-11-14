using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Venues.Infrastructure.Configurations;

public sealed class CustomAreaConfiguration : IEntityTypeConfiguration<CustomArea>
{
    public void Configure(EntityTypeBuilder<CustomArea> builder)
    {
        builder.ToTable("custom_areas");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.GovernorateCode).HasMaxLength(40).IsRequired();
        builder.Property(a => a.Code).HasMaxLength(60).IsRequired();
        builder.Property(a => a.NameAr).HasMaxLength(CustomArea.MaxNameLength).IsRequired();
        builder.Property(a => a.NameEn).HasMaxLength(CustomArea.MaxNameLength).IsRequired();
        builder.Property(a => a.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(a => a.IsFocus).HasDefaultValue(false).IsRequired();
        builder.Property(a => a.IsCustom).HasDefaultValue(true).IsRequired();
        builder.Property(a => a.DisplayOrder).HasDefaultValue(0).IsRequired();
        builder.Property(a => a.AddedByUserId).IsRequired(false);

        builder.HasIndex(a => new { a.GovernorateCode, a.NameAr }).IsUnique();
        builder.HasIndex(a => a.Code);
        builder.HasIndex(a => a.IsFocus);
        builder.HasIndex(a => a.IsActive);
    }
}
