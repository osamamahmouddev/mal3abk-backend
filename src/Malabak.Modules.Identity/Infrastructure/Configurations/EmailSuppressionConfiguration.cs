using Malabak.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Identity.Infrastructure.Configurations;

public sealed class EmailSuppressionConfiguration : IEntityTypeConfiguration<EmailSuppression>
{
    public void Configure(EntityTypeBuilder<EmailSuppression> builder)
    {
        builder.ToTable("email_suppressions");
        builder.HasKey(s => s.EmailHash);
        builder.Property(s => s.EmailHash).HasMaxLength(64);
        builder.Property(s => s.Reason).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.CreatedAtUtc).IsRequired();
    }
}
