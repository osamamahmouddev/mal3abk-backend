using System.Text.Json;
using Malabak.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Identity.Infrastructure.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.PhoneNumber)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(u => u.PhoneNumber)
            .IsUnique();

        builder.Property(u => u.Email)
            .HasMaxLength(256);

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter("\"Email\" IS NOT NULL");

        builder.Property(u => u.FullName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(u => u.PasswordHash)
            .HasMaxLength(500);

        builder.Property(u => u.SecurityStamp)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(u => u.IsPhoneNumberConfirmed)
            .IsRequired();

        builder.Property(u => u.IsEmailConfirmed)
            .IsRequired();

        builder.Property(u => u.TwoFactorEnabled)
            .IsRequired();

        builder.Property(u => u.CreatedAtUtc)
            .IsRequired();

        builder.Property(u => u.UpdatedAtUtc);

        builder.Property(u => u.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.RowVersion).IsRowVersion();

        var valueComparer = new ValueComparer<IReadOnlyCollection<string>>(
            (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList()); // List snapshot: matches the List backing field when EF re-materializes it

        builder.Property(u => u.Roles)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => (IReadOnlyCollection<string>)(JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()))
            .Metadata.SetValueComparer(valueComparer);

        builder.Property(u => u.City).HasMaxLength(User.MaxPlaceLength);
        builder.Property(u => u.Area).HasMaxLength(User.MaxPlaceLength);
        builder.Property(u => u.AvatarUrl).HasMaxLength(User.MaxAvatarUrlLength);
        builder.Property(u => u.SkillLevel).HasConversion<string>().HasMaxLength(20);
        builder.Property(u => u.Language).HasMaxLength(5).IsRequired().HasDefaultValue(UserLanguages.Arabic);
        builder.Ignore(u => u.IsOnboardingComplete);
        builder.Ignore(u => u.IsSuspended);
        builder.Property(u => u.SuspensionReason).HasMaxLength(User.MaxSuspensionReasonLength);
        builder.HasIndex(u => u.SuspendedAtUtc);

        // Positions stored as a short comma-separated list; snapshot as List so it fits the List backing field.
        var positionsComparer = new ValueComparer<IReadOnlyCollection<PlayerPosition>>(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            c => c.Aggregate(0, (h, p) => HashCode.Combine(h, p)),
            c => c.ToList());

        builder.Property(u => u.Positions)
            .HasConversion(
                v => string.Join(',', v),
                v => (IReadOnlyCollection<PlayerPosition>)v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<PlayerPosition>).ToList())
            .HasMaxLength(100)
            .Metadata.SetValueComparer(positionsComparer);

        builder.HasIndex(u => u.CreatedAtUtc);
        builder.HasIndex(u => u.IsDeleted);
    }
}
