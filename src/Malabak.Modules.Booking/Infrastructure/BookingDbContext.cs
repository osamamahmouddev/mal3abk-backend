using Microsoft.EntityFrameworkCore;
using Malabak.Modules.Booking.Domain;

namespace Malabak.Modules.Booking.Infrastructure;

public sealed class BookingDbContext : DbContext
{
    public const string Schema = "booking";

    public DbSet<Domain.Booking> Bookings => Set<Domain.Booking>();
    public DbSet<BookingStatusHistory> StatusHistories => Set<BookingStatusHistory>();

    public BookingDbContext(DbContextOptions<BookingDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Domain.Booking>(builder =>
        {
            builder.ToTable("bookings");
            builder.HasKey(b => b.Id);

            builder.Property(b => b.BookingReference)
                .HasMaxLength(32)
                .IsRequired();

            builder.HasIndex(b => b.BookingReference).IsUnique();

            builder.Property(b => b.PitchId).IsRequired();
            builder.Property(b => b.PlayerId).IsRequired();
            builder.Property(b => b.StartUtc).IsRequired();
            builder.Property(b => b.EndUtc).IsRequired();
            builder.Property(b => b.Status).IsRequired();

            builder.OwnsOne(b => b.TotalPrice, money =>
            {
                money.Property(m => m.Amount).HasPrecision(18, 2).HasColumnName("total_price_amount").IsRequired();
                money.Property(m => m.Currency).HasMaxLength(3).HasColumnName("total_price_currency").IsRequired();
            });

            builder.OwnsOne(b => b.DepositAmount, money =>
            {
                money.Property(m => m.Amount).HasPrecision(18, 2).HasColumnName("deposit_amount").IsRequired();
                money.Property(m => m.Currency).HasMaxLength(3).HasColumnName("deposit_currency").IsRequired();
            });

            builder.HasMany(b => b.History)
                .WithOne()
                .HasForeignKey(h => h.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(b => new { b.PitchId, b.StartUtc, b.EndUtc });
        });

        modelBuilder.Entity<BookingStatusHistory>(builder =>
        {
            builder.ToTable("booking_status_histories");
            builder.HasKey(h => h.Id);

            // The id is assigned in code. Without this, EF treats a history row with a pre-set key that it finds in a
            // stored booking's collection as an existing row and issues an UPDATE (0 rows) instead of an INSERT, so
            // every state change after the booking was first saved failed with a concurrency exception.
            builder.Property(h => h.Id).ValueGeneratedNever();
            builder.Property(h => h.Reason).HasMaxLength(500);
            builder.Property(h => h.ChangedByUserId).HasMaxLength(128).IsRequired();
        });
    }
}
