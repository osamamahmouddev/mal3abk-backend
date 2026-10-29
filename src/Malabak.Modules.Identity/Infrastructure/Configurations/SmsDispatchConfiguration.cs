using Malabak.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Identity.Infrastructure.Configurations;

public sealed class SmsDispatchConfiguration : IEntityTypeConfiguration<SmsDispatch>
{
    public void Configure(EntityTypeBuilder<SmsDispatch> builder)
    {
        builder.ToTable("sms_dispatches");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.MaskedPhone).HasMaxLength(20).IsRequired();
        builder.Property(d => d.PhoneHash).HasMaxLength(32).IsRequired();
        builder.Property(d => d.Template).HasMaxLength(40).IsRequired();
        builder.Property(d => d.Channel).HasMaxLength(20).IsRequired();
        builder.Property(d => d.Provider).HasMaxLength(50);
        builder.Property(d => d.ProviderMessageId).HasMaxLength(100);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.CostEgp).HasColumnType("numeric(10,4)");
        builder.Property(d => d.Error).HasMaxLength(300);
        builder.HasIndex(d => d.CreatedAtUtc);
        builder.HasIndex(d => d.ProviderMessageId);
        builder.HasIndex(d => new { d.PhoneHash, d.CreatedAtUtc });
    }
}
