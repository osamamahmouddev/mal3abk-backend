namespace Malabak.SharedKernel.Contracts;

public sealed record UpcomingBookingRef(Guid BookingId, string Reference, Guid PitchId, DateTime StartUtc, DateTime EndUtc, string Status);

/// <summary>Booking-module contract Venues uses to warn which future bookings a schedule change would affect.</summary>
public interface IUpcomingBookingsReader
{
    /// <summary>Bookings still holding their slot (pending, under review or confirmed) overlapping the range.</summary>
    Task<IReadOnlyList<UpcomingBookingRef>> GetActiveAsync(
        IReadOnlyCollection<Guid> pitchIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}

/// <summary>Venues-module contract the Booking module calls to validate a requested slot server-side.</summary>
public interface IPitchAvailabilityChecker
{
    /// <summary>Success when the pitch is open for the whole slot and it respects increments, notice and advance window.</summary>
    Task<Result> CheckSlotAsync(Guid pitchId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
}
