namespace Malabak.SharedKernel.Contracts;

/// <param name="HoldExpiresAtUtc">For bookings still awaiting the deposit: when the hold lapses and the slot frees up.</param>
public sealed record UpcomingBookingRef(
    Guid BookingId, string Reference, Guid PitchId, DateTime StartUtc, DateTime EndUtc, string Status, DateTime? HoldExpiresAtUtc = null);

/// <summary>Booking-module contract Venues uses to warn which future bookings a schedule change would affect.</summary>
public interface IUpcomingBookingsReader
{
    /// <summary>Bookings still holding their slot (pending, under review or confirmed) overlapping the range.</summary>
    Task<IReadOnlyList<UpcomingBookingRef>> GetActiveAsync(
        IReadOnlyCollection<Guid> pitchIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// Venues-module contract to drop cached availability after a change other modules make (bookings created, holds placed
/// or released, status changes). Call it after the change is committed.
/// </summary>
public interface IAvailabilityInvalidator
{
    Task InvalidatePitchAsync(Guid pitchId, CancellationToken cancellationToken = default);

    Task InvalidateVenueAsync(Guid venueId, CancellationToken cancellationToken = default);
}

/// <summary>Venues-module contract the Booking module calls to validate a requested slot server-side.</summary>
public interface IPitchAvailabilityChecker
{
    /// <summary>Success when the pitch is open for the whole slot and it respects increments, notice and advance window.</summary>
    Task<Result> CheckSlotAsync(Guid pitchId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
}
