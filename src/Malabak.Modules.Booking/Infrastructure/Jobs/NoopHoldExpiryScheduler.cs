using Malabak.Modules.Booking.Application;

namespace Malabak.Modules.Booking.Infrastructure.Jobs;

/// <summary>
/// Fallback used when the host intentionally disables Hangfire.
/// Booking hold expiry is still enforced by the database-backed expiration sweep
/// when a job worker is available; this implementation keeps API startup and
/// booking creation available on hosts without a background worker.
/// </summary>
public sealed class NoopHoldExpiryScheduler : IHoldExpiryScheduler
{
    public void ScheduleHoldExpiry(Guid bookingId, DateTime holdExpiresAtUtc)
    {
        // No background worker is configured for this host.
    }
}
