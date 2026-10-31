namespace Malabak.Modules.Booking.Domain;

/// <summary>Platform rules for slot holds. Venues tune the hold length through their booking policy.</summary>
public static class BookingHoldRules
{
    /// <summary>
    /// Most bookings one player may have waiting for a deposit at the same time (SECURITY.md section 11, slot hoarding).
    /// ADR-0008 says 3; SECURITY.md is stricter and wins (AGENTS.md).
    /// </summary>
    public const int MaxPendingBookingsPerPlayer = 2;
}
