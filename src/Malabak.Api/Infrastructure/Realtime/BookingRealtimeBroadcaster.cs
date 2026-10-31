using Malabak.Modules.Booking.Application;
using Microsoft.AspNetCore.SignalR;

namespace Malabak.Api.Infrastructure.Realtime;

public sealed class BookingRealtimeBroadcaster : IBookingRealtimeBroadcaster
{
    private readonly IHubContext<BookingHub, IBookingHubClient> _hubContext;
    private readonly ILogger<BookingRealtimeBroadcaster> _logger;

    public BookingRealtimeBroadcaster(
        IHubContext<BookingHub, IBookingHubClient> hubContext,
        ILogger<BookingRealtimeBroadcaster> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task BroadcastSlotStatusChangedAsync(
        Guid venueId,
        Guid pitchId,
        DateTime startUtc,
        DateTime endUtc,
        string status,
        CancellationToken cancellationToken = default)
    {
        var date = DateOnly.FromDateTime(startUtc);
        var groupName = BookingHub.VenueDateGroupName(venueId, date);

        var notification = new SlotStatusChangedNotification(pitchId, startUtc, endUtc, status);

        try
        {
            await _hubContext.Clients.Group(groupName).SlotStatusChanged(notification);
            _logger.LogDebug("Broadcasted SlotStatusChanged to {GroupName} (Pitch: {PitchId}, Status: {Status})",
                groupName, pitchId, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast SlotStatusChanged to {GroupName}", groupName);
        }
    }

    public async Task BroadcastBookingUpdatedAsync(
        Guid userId,
        Guid venueId,
        Guid bookingId,
        string reference,
        string status,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var notification = new BookingUpdatedNotification(bookingId, reference, venueId, status, updatedAtUtc);

        try
        {
            // Send to the player privately
            await _hubContext.Clients.Group(BookingHub.UserGroupName(userId)).BookingUpdated(notification);

            // Send to the venue staff room
            await _hubContext.Clients.Group(BookingHub.VenueStaffGroupName(venueId)).BookingUpdated(notification);

            _logger.LogDebug("Broadcasted BookingUpdated for booking {Reference} (Status: {Status})", reference, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast BookingUpdated for booking {Reference}", reference);
        }
    }
}
