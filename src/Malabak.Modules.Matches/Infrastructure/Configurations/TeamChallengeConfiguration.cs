using Malabak.Modules.Matches.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Matches.Infrastructure.Configurations;

public sealed class TeamChallengeConfiguration : IEntityTypeConfiguration<TeamChallenge>
{
    public void Configure(EntityTypeBuilder<TeamChallenge> builder)
    {
        builder.ToTable("team_challenges");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.ChallengerTeamId)
            .IsRequired();

        builder.Property(c => c.ChallengedTeamId)
            .IsRequired();

        builder.Property(c => c.ChallengerCaptainId)
            .IsRequired();

        builder.Property(c => c.ProposedDateUtc)
            .IsRequired();

        builder.Property(c => c.ProposedDurationMinutes)
            .HasDefaultValue(TeamChallenge.DefaultDurationMinutes)
            .IsRequired();

        builder.Property(c => c.ProposedPitchId);

        builder.Property(c => c.ProposedVenueId);

        builder.Property(c => c.ProposedVenueName)
            .HasMaxLength(200);

        builder.Property(c => c.Notes)
            .HasMaxLength(1000);

        builder.Property(c => c.Status)
            .IsRequired();

        builder.Property(c => c.CurrentTurnTeamId)
            .IsRequired();

        builder.Property(c => c.AcceptedAtUtc);

        builder.Property(c => c.DeclinedAtUtc);

        builder.Property(c => c.DeclinedReason)
            .HasMaxLength(500);

        builder.Property(c => c.MatchId);

        builder.Property(c => c.RowVersion)
            .IsRowVersion();

        builder.HasIndex(c => new { c.ChallengerTeamId, c.Status });
        builder.HasIndex(c => new { c.ChallengedTeamId, c.Status });
        builder.HasIndex(c => c.ProposedDateUtc);
    }
}
