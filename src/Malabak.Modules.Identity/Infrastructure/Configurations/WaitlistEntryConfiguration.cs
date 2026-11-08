using Malabak.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Identity.Infrastructure.Configurations;

public sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> builder)
    {
        builder.ToTable("waitlist_entries");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Audience)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(w => w.ChosenPlan)
            .HasMaxLength(50);

        builder.Property(w => w.PhoneEncrypted)
            .IsRequired();

        builder.Property(w => w.PhoneBlindIndex)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(w => w.IpAddress)
            .HasMaxLength(45);

        builder.Property(w => w.UserAgent)
            .HasMaxLength(500);

        builder.HasIndex(w => w.PhoneBlindIndex)
            .IsUnique();

        builder.HasIndex(w => w.CreatedAtUtc);
        builder.HasIndex(w => w.Audience);
    }
}
