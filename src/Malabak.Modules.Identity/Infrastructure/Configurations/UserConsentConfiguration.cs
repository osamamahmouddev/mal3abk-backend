using Malabak.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Identity.Infrastructure.Configurations;

public sealed class UserConsentConfiguration : IEntityTypeConfiguration<UserConsent>
{
    public void Configure(EntityTypeBuilder<UserConsent> builder)
    {
        builder.ToTable("user_consents");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Version).HasMaxLength(UserConsent.MaxVersionLength);
        builder.HasIndex(c => new { c.UserId, c.Type, c.RecordedAtUtc });
    }
}
