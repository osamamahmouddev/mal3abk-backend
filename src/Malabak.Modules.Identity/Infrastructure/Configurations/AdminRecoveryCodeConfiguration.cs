using Malabak.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Identity.Infrastructure.Configurations;

public sealed class AdminRecoveryCodeConfiguration : IEntityTypeConfiguration<AdminRecoveryCode>
{
    public void Configure(EntityTypeBuilder<AdminRecoveryCode> builder)
    {
        builder.ToTable("admin_recovery_codes");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.CodeHash).HasMaxLength(64).IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.HasIndex(c => new { c.UserId, c.CodeHash }).IsUnique();
    }
}
