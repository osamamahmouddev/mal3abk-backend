using Malabak.Modules.Booking.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Malabak.Modules.Booking.Application.Abstractions;

/// <summary>
/// The booking data the use cases work on. The Application layer depends on this, never on the concrete
/// <c>BookingDbContext</c> in Infrastructure (architecture rule: Application must not depend on Infrastructure).
/// </summary>
public interface IBookingDbContext
{
    DbSet<Domain.Booking> Bookings { get; }

    DbSet<BookingStatusHistory> StatusHistories { get; }

    DbSet<PaymentProof> PaymentProofs { get; }

    DbSet<BookingRefund> BookingRefunds { get; }

    DatabaseFacade Database { get; }

    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
