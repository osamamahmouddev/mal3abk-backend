using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Malabak.Modules.Booking.Application;

/// <summary>Recognises the PostgreSQL violations the booking flow turns into business outcomes instead of a 500.</summary>
public static class BookingPersistenceErrors
{
    /// <summary>The unique index on <c>BookingReference</c> (see <c>BookingDbContext</c>).</summary>
    public const string BookingReferenceIndex = "IX_bookings_BookingReference";

    private const string _uniqueViolation = "23505";
    private const string _exclusionViolation = "23P01";

    /// <summary>The overlap constraint rejected an active booking that overlaps another one on the same pitch.</summary>
    public static bool IsSlotOverlap(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: _exclusionViolation }
        || exception.InnerException is PostgresException { ConstraintName: BookingConstraints.NoOverlappingActiveBookings };

    /// <summary>A generated reference already exists on another booking: retry with a new one.</summary>
    public static bool IsReferenceCollision(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: _uniqueViolation, ConstraintName: BookingReferenceIndex };
}
