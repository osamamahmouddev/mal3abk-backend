namespace Malabak.Modules.Booking.Application;

/// <summary>Names of database constraints the use cases translate into business outcomes.</summary>
public static class BookingConstraints
{
    /// <summary>Exclusion constraint: two active bookings of one pitch can never overlap.</summary>
    public const string NoOverlappingActiveBookings = "exclude_overlapping_active_bookings";
}
