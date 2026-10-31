using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Infrastructure;

public sealed class BookingDbContext : BaseDbContext, IBookingDbContext
{
    public const string Schema = "booking";
    public override string ModuleSchema => Schema;
    public const string ExclusionConstraintName = BookingConstraints.NoOverlappingActiveBookings;

    public DbSet<Domain.Booking> Bookings => Set<Domain.Booking>();
    public DbSet<BookingStatusHistory> StatusHistories => Set<BookingStatusHistory>();
    public DbSet<PaymentProof> PaymentProofs => Set<PaymentProof>();
    public DbSet<BookingRefund> BookingRefunds => Set<BookingRefund>();
    public DbSet<VenueCustomerNote> CustomerNotes => Set<VenueCustomerNote>();

    public BookingDbContext(DbContextOptions<BookingDbContext> options, IClock? clock = null)
        : base(options, clock)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.Entity<Domain.Booking>(builder =>
        {
            builder.ToTable("bookings");
            builder.HasKey(b => b.Id);

            builder.Property(b => b.BookingReference)
                .HasMaxLength(32)
                .IsRequired();

            builder.HasIndex(b => b.BookingReference).IsUnique();

            builder.Property(b => b.VenueId).IsRequired();
            builder.Property(b => b.PitchId).IsRequired();
            builder.Property(b => b.PlayerId).IsRequired();
            builder.Property(b => b.StartUtc).IsRequired();
            builder.Property(b => b.EndUtc).IsRequired();
            builder.Property(b => b.Status).IsRequired();

            builder.Property(b => b.CancellationReason).HasMaxLength(500);
            builder.Property(b => b.RejectionReason).HasMaxLength(500);
            builder.Property(b => b.CustomerName).HasMaxLength(200);
            builder.Property(b => b.CustomerPhone).HasMaxLength(32);

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

            builder.HasMany(b => b.Proofs)
                .WithOne()
                .HasForeignKey(p => p.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(b => b.Refunds)
                .WithOne()
                .HasForeignKey(r => r.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(b => new { b.VenueId, b.StartUtc });
            builder.HasIndex(b => new { b.PlayerId, b.Status });
            builder.HasIndex(b => new { b.PitchId, b.StartUtc, b.EndUtc });
        });

        modelBuilder.Entity<BookingStatusHistory>(builder =>
        {
            builder.ToTable("booking_status_histories");
            builder.HasKey(h => h.Id);

            // Ids are assigned in code. Without this, EF treats a child with a pre-set generated key that it finds in a
            // stored booking's collection as an existing row and issues an UPDATE (0 rows) instead of an INSERT, so every
            // later change to a saved booking failed with a concurrency exception.
            builder.Property(h => h.Id).ValueGeneratedNever();
            builder.Property(h => h.Reason).HasMaxLength(500);
            builder.Property(h => h.ChangedByUserId).HasMaxLength(128).IsRequired();
            builder.Property(h => h.MetadataJson).HasColumnType("text");
            builder.HasIndex(h => h.BookingId);
        });

        modelBuilder.Entity<PaymentProof>(builder =>
        {
            builder.ToTable("payment_proofs");
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.Property(p => p.FileKey).HasMaxLength(500).IsRequired();
            builder.Property(p => p.OriginalFileName).HasMaxLength(255).IsRequired();
            builder.Property(p => p.ContentType).HasMaxLength(100).IsRequired();
            builder.Property(p => p.Sha256Hash).HasMaxLength(64).IsRequired();
            builder.Property(p => p.UploadedByUserId).HasMaxLength(128).IsRequired();
            builder.Property(p => p.AntivirusResult).HasMaxLength(255);
            builder.Property(p => p.RejectionReason).HasMaxLength(500);
            builder.Property(p => p.ReviewedByUserId).HasMaxLength(128);

            builder.HasIndex(p => p.BookingId);

            // One receipt (by content hash) may be active on one booking only (SECURITY.md section 11). A rejected proof no
            // longer counts, so the player can send the corrected one; proofs from before hashing have no hash and are
            // exempt. The handler checks first for a friendly error; this index is what holds when two requests race.
            builder.HasIndex(p => p.Sha256Hash)
                .IsUnique()
                .HasFilter("\"Sha256Hash\" <> '' AND \"IsApproved\" IS DISTINCT FROM FALSE");
        });

        modelBuilder.Entity<BookingRefund>(builder =>
        {
            builder.ToTable("booking_refunds");
            builder.HasKey(r => r.Id);
            builder.Property(r => r.Id).ValueGeneratedNever();
            builder.Property(r => r.ExpectedRefundAmount).HasPrecision(18, 2).IsRequired();
            builder.Property(r => r.ActualRefundAmount).HasPrecision(18, 2);
            builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
            builder.Property(r => r.Reason).HasMaxLength(500);
            builder.Property(r => r.RecordedByUserId).HasMaxLength(128);
            builder.Property(r => r.Notes).HasMaxLength(1000);

            // One refund record per booking: it tracks what is owed and what was paid, so a duplicate would double count.
            builder.HasIndex(r => r.BookingId).IsUnique();
        });

        modelBuilder.Entity<VenueCustomerNote>(builder =>
        {
            builder.ToTable("venue_customer_notes");
            builder.HasKey(n => n.Id);
            builder.Property(n => n.Id).ValueGeneratedNever();
            builder.Property(n => n.CustomerKey).HasMaxLength(80).IsRequired();
            builder.Property(n => n.TextCiphertext).IsRequired();
            builder.HasIndex(n => new { n.VenueId, n.CustomerKey, n.CreatedAtUtc });
        });
    }
}
