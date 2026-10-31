using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Malabak.Modules.Booking.Application;

/// <summary>Recognises the PostgreSQL violations the booking flow turns into business outcomes instead of a 500.</summary>
public static class BookingPersistenceErrors
{
    /// <summary>The unique index on <c>BookingReference</c> (see <c>BookingDbContext</c>).</summary>
    public const string BookingReferenceIndex = "IX_bookings_BookingReference";

    /// <summary>The partial unique index that stops one receipt (by content hash) being active on two bookings.</summary>
    public const string ProofHashIndex = "IX_payment_proofs_Sha256Hash";

    private const string _uniqueViolation = "23505";
    private const string _exclusionViolation = "23P01";

    /// <summary>The overlap constraint rejected an active booking that overlaps another one on the same pitch.</summary>
    public static bool IsSlotOverlap(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: _exclusionViolation }
        || exception.InnerException is PostgresException { ConstraintName: BookingConstraints.NoOverlappingActiveBookings };

    /// <summary>The same receipt image is already active on another booking.</summary>
    public static bool IsDuplicateProofHash(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: _uniqueViolation, ConstraintName: ProofHashIndex };

    /// <summary>A generated reference already exists on another booking: retry with a new one.</summary>
    public static bool IsReferenceCollision(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: _uniqueViolation, ConstraintName: BookingReferenceIndex };
}
