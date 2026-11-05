using Malabak.Modules.Teams.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Teams.Infrastructure.Configurations;

public sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("teams");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(t => t.Slug)
            .HasMaxLength(120)
            .IsRequired();

        builder.HasIndex(t => t.Slug)
            .IsUnique();

        builder.Property(t => t.Description)
            .HasMaxLength(500);

        builder.Property(t => t.LogoUrl)
            .HasMaxLength(1000);

        builder.Property(t => t.CoverUrl)
            .HasMaxLength(1000);

        builder.Property(t => t.City)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(t => t.Governorate)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(t => t.District)
            .HasMaxLength(100);

        builder.Property(t => t.CaptainId)
            .IsRequired();

        builder.HasIndex(t => t.CaptainId);

        builder.Property(t => t.InviteCode)
            .HasMaxLength(12)
            .IsRequired();

        builder.HasIndex(t => t.InviteCode);

        builder.Property(t => t.InviteCodeExpiresAtUtc);

        builder.Property(t => t.IsRecruiting)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(t => t.IsOpenForChallenges)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(t => t.IsPublic)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(t => t.Rating)
            .HasDefaultValue(1500.0)
            .IsRequired();

        builder.Property(t => t.RatingDeviation)
            .HasDefaultValue(350.0)
            .IsRequired();

        builder.Property(t => t.RatingVolatility)
            .HasDefaultValue(0.06)
            .IsRequired();

        builder.Property(t => t.MatchesPlayed)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(t => t.Wins)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(t => t.Draws)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(t => t.Losses)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(t => t.GoalsFor)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(t => t.GoalsAgainst)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(t => t.RowVersion)
            .IsRowVersion();

        builder.HasMany(t => t.Members)
            .WithOne()
            .HasForeignKey(m => m.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(t => t.Members)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
