using Malabak.Modules.Teams.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Teams.Infrastructure.Configurations;

public sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("team_members");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.TeamId)
            .IsRequired();

        builder.Property(m => m.PlayerId)
            .IsRequired();

        builder.HasIndex(m => m.PlayerId);

        builder.Property(m => m.Role)
            .IsRequired();

        builder.Property(m => m.JerseyNumber);

        builder.Property(m => m.Position)
            .HasMaxLength(50);

        builder.Property(m => m.JoinedAtUtc)
            .IsRequired();

        builder.Property(m => m.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(m => m.RejoinBlocked)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(m => m.RowVersion)
            .IsRowVersion();

        builder.HasIndex(m => new { m.TeamId, m.PlayerId });
    }
}
