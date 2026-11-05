using Malabak.Modules.Teams.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Teams.Infrastructure.Configurations;

public sealed class TeamJoinRequestConfiguration : IEntityTypeConfiguration<TeamJoinRequest>
{
    public void Configure(EntityTypeBuilder<TeamJoinRequest> builder)
    {
        builder.ToTable("team_join_requests");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.TeamId)
            .IsRequired();

        builder.Property(r => r.PlayerId)
            .IsRequired();

        builder.Property(r => r.Message)
            .HasMaxLength(500);

        builder.Property(r => r.Status)
            .IsRequired();

        builder.Property(r => r.ReviewedAtUtc);

        builder.Property(r => r.ReviewedByPlayerId);

        builder.Property(r => r.RowVersion)
            .IsRowVersion();

        builder.HasIndex(r => new { r.TeamId, r.PlayerId, r.Status });
        builder.HasIndex(r => r.PlayerId);
    }
}
