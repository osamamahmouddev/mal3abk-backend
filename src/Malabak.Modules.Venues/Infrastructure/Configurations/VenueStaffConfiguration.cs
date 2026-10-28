using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Venues.Infrastructure.Configurations;

public sealed class VenueStaffMemberConfiguration : IEntityTypeConfiguration<VenueStaffMember>
{
    public void Configure(EntityTypeBuilder<VenueStaffMember> builder)
    {
        builder.ToTable("staff_members");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(m => m.RowVersion).IsRowVersion();

        // A user is at most one active member per venue.
        builder.HasIndex(m => new { m.VenueId, m.UserId }).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(m => m.UserId);
    }
}

public sealed class VenueStaffInvitationConfiguration : IEntityTypeConfiguration<VenueStaffInvitation>
{
    public void Configure(EntityTypeBuilder<VenueStaffInvitation> builder)
    {
        builder.ToTable("staff_invitations");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Email).HasMaxLength(256);
        builder.Property(i => i.PhoneNumber).HasMaxLength(32);
        builder.Property(i => i.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(i => i.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(i => i.RowVersion).IsRowVersion();
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => new { i.VenueId, i.Status });
    }
}
