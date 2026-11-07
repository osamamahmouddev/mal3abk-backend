namespace Malabak.SharedKernel.Contracts;

/// <summary>
/// Booking figures of one venue over a period (by booking start time). <c>ConfirmedValue</c> is the total price of bookings
/// that were confirmed or completed: what the venue earned on the platform, not money the platform collected.
/// </summary>
public sealed record VenueBookingStats(
    int Total, int Confirmed, int Completed, int Cancelled, int NoShow, int Pending, decimal ConfirmedValue, string Currency, DateTime? LastBookingStartUtc);

/// <summary>Implemented by the Booking module so the admin venue page (TASK-702) never reads booking tables directly.</summary>
public interface IVenueBookingStatsReader
{
    Task<VenueBookingStats> GetAsync(Guid venueId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}
