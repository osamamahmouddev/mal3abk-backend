using Malabak.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Notifications.Infrastructure;

public sealed class NotificationsDbContext : DbContext
{
    public const string Schema = NotificationsModule.Schema;

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> Preferences => Set<NotificationPreference>();
    public DbSet<NotificationOutboxMessage> Outbox => Set<NotificationOutboxMessage>();

    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Notification>(b =>
        {
            b.ToTable("notifications");
            b.HasKey(n => n.Id);
            b.Property(n => n.Type).HasMaxLength(60).IsRequired();
            b.Property(n => n.Category).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(n => n.DataJson).IsRequired();
            b.Property(n => n.DeepLink).HasMaxLength(300);
            b.Property(n => n.DedupeKey).HasMaxLength(200).IsRequired();
            b.Property(n => n.RowVersion).IsRowVersion();
            b.Ignore(n => n.Data);
            b.HasIndex(n => new { n.UserId, n.DedupeKey }).IsUnique();
            b.HasIndex(n => new { n.UserId, n.CreatedAtUtc });
            b.HasIndex(n => new { n.UserId, n.ReadAtUtc });
        });

        modelBuilder.Entity<NotificationPreference>(b =>
        {
            b.ToTable("preferences");
            b.HasKey(p => new { p.UserId, p.Category });
            b.Property(p => p.Category).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<NotificationOutboxMessage>(b =>
        {
            b.ToTable("outbox");
            b.HasKey(m => m.Id);
            b.Property(m => m.Type).HasMaxLength(60).IsRequired();
            b.Property(m => m.DataJson).IsRequired();
            b.Property(m => m.DedupeKey).HasMaxLength(200).IsRequired();
            b.Property(m => m.Error).HasMaxLength(500);
            b.Ignore(m => m.Data);
            b.HasIndex(m => new { m.ProcessedAtUtc, m.CreatedAtUtc });
        });
    }
}
