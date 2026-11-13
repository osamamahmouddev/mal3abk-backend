namespace Malabak.Modules.Booking.Domain;

/// <summary>Platform rules for slot holds. Venues tune the hold length through their booking policy.</summary>
public static class BookingHoldRules
{
    /// <summary>
    /// Most bookings one player may have waiting for a deposit at the same time (SECURITY.md section 11, slot hoarding).
    /// ADR-0008 says 3; SECURITY.md is stricter and wins (AGENTS.md).
    /// </summary>
    public const int MaxPendingBookingsPerPlayer = 2;

    /// <summary>Holds that lapse without any payment proof, within <see cref="PenaltyWindow"/>, that trigger the lock (ADR-0008 section 1).</summary>
    public const int ExpiredHoldsForPenalty = 3;

    public static readonly TimeSpan PenaltyWindow = TimeSpan.FromHours(24);

    /// <summary>How long a player cannot create bookings once the penalty applies; counted from the last hold that lapsed.</summary>
    public static readonly TimeSpan PenaltyLockDuration = TimeSpan.FromHours(6);
}
