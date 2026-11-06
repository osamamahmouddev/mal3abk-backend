using System.Text.Json;
using Malabak.Modules.Subscriptions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Malabak.Modules.Subscriptions.Infrastructure;

public sealed class SubscriptionsDbContext : DbContext
{
    public const string Schema = SubscriptionsModule.Schema;

    public DbSet<SubscriptionPlan> Plans => Set<SubscriptionPlan>();
    public DbSet<OwnerSubscription> Subscriptions => Set<OwnerSubscription>();
    public DbSet<SubscriptionEvent> Events => Set<SubscriptionEvent>();
    public DbSet<SubscriptionPayment> Payments => Set<SubscriptionPayment>();
    public DbSet<Invoice> Invoices => Set<Invoice>();

    public SubscriptionsDbContext(DbContextOptions<SubscriptionsDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasSequence<long>(InvoiceNumberGenerator.Sequence).StartsAt(1).IncrementsBy(1);

        var listComparer = new ValueComparer<IReadOnlyList<string>>(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            c => c.Aggregate(0, (h, v) => HashCode.Combine(h, v.GetHashCode())),
            c => c.ToList());

        modelBuilder.Entity<SubscriptionPlan>(b =>
        {
            b.ToTable("plans");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Code).HasMaxLength(40).IsRequired();
            b.HasIndex(p => p.Code).IsUnique();
            b.Property(p => p.Name).HasMaxLength(100).IsRequired();
            b.Property(p => p.Description).HasMaxLength(1000);
            b.Property(p => p.MonthlyPrice).HasPrecision(12, 2);
            b.Property(p => p.AnnualPrice).HasPrecision(12, 2);
            b.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(p => p.Features)
                .HasField("_features")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                .Metadata.SetValueComparer(listComparer);
        });

        modelBuilder.Entity<OwnerSubscription>(b =>
        {
            b.ToTable("owner_subscriptions");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).ValueGeneratedNever();
            // One subscription row per owner: changes happen on it, history lives in subscription_events.
            b.HasIndex(s => s.OwnerId).IsUnique();
            b.HasIndex(s => new { s.Status, s.CurrentPeriodEndUtc });
            b.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(s => s.Cycle).HasConversion<string>().HasMaxLength(10);
            b.Property(s => s.StatusReason).HasMaxLength(500);
            b.Property(s => s.RowVersion).IsRowVersion();
            b.HasMany(s => s.Events).WithOne().HasForeignKey(e => e.SubscriptionId);
            b.Navigation(s => s.Events).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<SubscriptionEvent>(b =>
        {
            b.ToTable("subscription_events");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Type).HasConversion<string>().HasMaxLength(30);
            b.Property(e => e.Note).HasMaxLength(500);
            b.HasIndex(e => new { e.SubscriptionId, e.OccurredAtUtc });
        });

        modelBuilder.Entity<SubscriptionPayment>(b =>
        {
            b.ToTable("payments");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Amount).HasPrecision(12, 2);
            b.Property(p => p.Currency).HasMaxLength(3);
            b.Property(p => p.Method).HasConversion<string>().HasMaxLength(20);
            b.Property(p => p.Reference).HasMaxLength(100);
            b.Property(p => p.Notes).HasMaxLength(500);
            b.HasIndex(p => new { p.OwnerId, p.PaidAtUtc });
            b.HasIndex(p => p.PaidAtUtc);
        });

        modelBuilder.Entity<Invoice>(b =>
        {
            b.ToTable("invoices");
            b.HasKey(i => i.Id);
            b.Property(i => i.Id).ValueGeneratedNever();
            b.Property(i => i.Number).HasMaxLength(30).IsRequired();
            b.HasIndex(i => i.Number).IsUnique();
            b.HasIndex(i => new { i.OwnerId, i.IssuedAtUtc });
            b.Property(i => i.PlanCode).HasMaxLength(40);
            b.Property(i => i.PlanName).HasMaxLength(100);
            b.Property(i => i.Cycle).HasConversion<string>().HasMaxLength(10);
            b.Property(i => i.Amount).HasPrecision(12, 2);
            b.Property(i => i.Currency).HasMaxLength(3);
            b.Property(i => i.Status).HasConversion<string>().HasMaxLength(10);
            b.Property(i => i.VoidReason).HasMaxLength(500);
        });
    }
}
