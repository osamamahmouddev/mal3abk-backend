using Malabak.Modules.Payments.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Payments.Infrastructure;

public sealed class PaymentsDbContext : BaseDbContext
{
    public const string Schema = PaymentsModule.Schema;
    public override string ModuleSchema => Schema;

    public DbSet<PaymentTransaction> Transactions => Set<PaymentTransaction>();

    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options, IClock? clock = null)
        : base(options, clock)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PaymentTransaction>(builder =>
        {
            builder.ToTable("payment_transactions");
            builder.HasKey(t => t.Id);

            builder.Property(t => t.BookingId).IsRequired();
            builder.Property(t => t.PlayerId).IsRequired();
            builder.Property(t => t.Amount).HasPrecision(18, 2).IsRequired();
            builder.Property(t => t.Currency).HasMaxLength(3).IsRequired();
            builder.Property(t => t.Provider).HasMaxLength(50).IsRequired();
            builder.Property(t => t.ProviderTransactionId).HasMaxLength(100);
            builder.Property(t => t.ProviderOrderId).HasMaxLength(100);
            builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(t => t.PaymentMethod).HasMaxLength(50);
            builder.Property(t => t.RawWebhookPayload).HasColumnType("text");
            builder.Property(t => t.CreatedAtUtc).IsRequired();

            builder.HasIndex(t => t.BookingId);
            builder.HasIndex(t => t.ProviderOrderId);
            builder.HasIndex(t => t.ProviderTransactionId);
        });
    }
}
