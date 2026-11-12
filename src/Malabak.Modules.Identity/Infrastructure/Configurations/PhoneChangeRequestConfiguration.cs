using Malabak.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Identity.Infrastructure.Configurations;

public sealed class PhoneChangeRequestConfiguration : IEntityTypeConfiguration<PhoneChangeRequest>
{
    public void Configure(EntityTypeBuilder<PhoneChangeRequest> builder)
    {
        builder.ToTable("phone_change_requests");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.UserId)
            .IsRequired();

        builder.Property(p => p.OldPhoneNumber)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(p => p.NewPhoneNumber)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(p => p.Status)
            .IsRequired();

        builder.Property(p => p.OldPhoneOtpHash)
            .HasMaxLength(128);

        builder.Property(p => p.NewPhoneOtpHash)
            .HasMaxLength(128);

        builder.Property(p => p.RecoveryTokenHash)
            .HasMaxLength(128);

        builder.Property(p => p.AttemptsOld)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(p => p.AttemptsNew)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(p => p.ExpiresAtUtc)
            .IsRequired();

        builder.Property(p => p.CompletedAtUtc);

        builder.Property(p => p.CreatedAtUtc)
            .IsRequired();

        builder.Property(p => p.UpdatedAtUtc);

        builder.Property(p => p.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.UserId);
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.ExpiresAtUtc);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
