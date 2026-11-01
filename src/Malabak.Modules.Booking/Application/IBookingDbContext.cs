using Microsoft.EntityFrameworkCore;
using Malabak.Modules.Booking.Domain;

namespace Malabak.Modules.Booking.Application;

public interface IBookingDbContext
{
    public const string ExclusionConstraintName = "exclude_overlapping_active_bookings";

    DbSet<Domain.Booking> Bookings { get; }
    DbSet<BookingStatusHistory> StatusHistories { get; }
    DbSet<PaymentProof> PaymentProofs { get; }
    DbSet<BookingRefund> BookingRefunds { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
