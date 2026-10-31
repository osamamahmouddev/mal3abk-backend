using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application;

/// <summary>
/// Concurrency helpers shared by the use cases that create bookings. They must run inside an open transaction: the
/// advisory locks are released when it ends.
/// </summary>
internal static class BookingConcurrency
{
    /// <summary>
    /// One booking request per player at a time, so the pending-hold limit cannot be raced past by parallel requests.
    /// </summary>
    public static Task LockPlayerAsync(IBookingDbContext db, Guid playerId, CancellationToken cancellationToken) =>
        LockAsync(db, $"booking:player:{playerId:N}", cancellationToken);

    /// <summary>
    /// One booking request per pitch at a time. The exclusion constraint alone is correct but, when many requests insert
    /// overlapping rows at once, PostgreSQL may abort one with a deadlock instead of an exclusion violation. Serialising
    /// writers per pitch removes that, and the constraint stays the final guard.
    /// </summary>
    public static Task LockPitchAsync(IBookingDbContext db, Guid pitchId, CancellationToken cancellationToken) =>
        LockAsync(db, $"booking:pitch:{pitchId:N}", cancellationToken);

    private static Task LockAsync(IBookingDbContext db, string key, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);

    /// <summary>
    /// A hold whose time is up still occupies its slot, and its owner's quota, until the expiry job runs. Availability
    /// already treats it as free, so release it here instead of rejecting a booking the caller was told they can make.
    /// Releases the holds on the given slot and, when a player is given, all of that player's lapsed holds.
    /// </summary>
    public static async Task ReleaseLapsedHoldsAsync(
        IBookingDbContext db, Guid? playerId, Guid pitchId, DateTime startUtc, DateTime endUtc, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var lapsed = await db.Bookings
            .Include(b => b.History)
            .Where(b => b.Status == BookingStatus.PendingDeposit && b.HoldExpiresAtUtc <= nowUtc
                && ((playerId != null && b.PlayerId == playerId) || (b.PitchId == pitchId && b.StartUtc < endUtc && b.EndUtc > startUtc)))
            .ToListAsync(cancellationToken);
        if (lapsed.Count == 0)
        {
            return;
        }

        foreach (var booking in lapsed)
        {
            booking.Expire(nowUtc);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
