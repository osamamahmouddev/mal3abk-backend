namespace Malabak.Modules.Booking.Application;

public interface IBookingRealtimeBroadcaster
{
    Task BroadcastSlotStatusChangedAsync(
        Guid venueId,
        Guid pitchId,
        DateTime startUtc,
        DateTime endUtc,
        string status,
        CancellationToken cancellationToken = default);

    Task BroadcastBookingUpdatedAsync(
        Guid userId,
        Guid venueId,
        Guid bookingId,
        string reference,
        string status,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default);
}
