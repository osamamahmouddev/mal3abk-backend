using Malabak.Modules.Booking.Application;
using Malabak.SharedKernel;
using Microsoft.AspNetCore.SignalR;

namespace Malabak.Api.Infrastructure.Realtime;

public sealed class BookingRealtimeBroadcaster : IBookingRealtimeBroadcaster
{
    private readonly IHubContext<BookingHub, IBookingHubClient> _hubContext;
    private readonly IClock _clock;
    private readonly ILogger<BookingRealtimeBroadcaster> _logger;

    public BookingRealtimeBroadcaster(
        IHubContext<BookingHub, IBookingHubClient> hubContext,
        IClock clock,
        ILogger<BookingRealtimeBroadcaster> logger)
    {
        _hubContext = hubContext;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// The venue-day rooms a slot belongs to. Clients watch the venue's business date in Cairo, and a slot after
    /// midnight belongs to that calendar day or, when the venue is open past midnight, to the day before; both are
    /// told and each client keeps only slots it shows.
    /// </summary>
    public static IReadOnlyList<string> SlotGroupNames(Guid venueId, DateTime startUtc, TimeZoneInfo cairo)
    {
        var cairoDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), cairo));
        return
        [
            BookingHub.VenueDateGroupName(venueId, cairoDate),
            BookingHub.VenueDateGroupName(venueId, cairoDate.AddDays(-1)),
        ];
    }

    public async Task BroadcastSlotStatusChangedAsync(
        Guid venueId,
        Guid pitchId,
        DateTime startUtc,
        DateTime endUtc,
        string status,
        CancellationToken cancellationToken = default)
    {
        var groups = SlotGroupNames(venueId, startUtc, _clock.CairoTimeZone);
        var notification = new SlotStatusChangedNotification(pitchId, startUtc, endUtc, status, venueId, _clock.UtcNow);

        try
        {
            await _hubContext.Clients.Groups(groups).SlotStatusChanged(notification);
            _logger.LogDebug("Broadcasted SlotStatusChanged to {Groups} (Pitch: {PitchId}, Status: {Status})",
                groups, pitchId, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast SlotStatusChanged to {Groups}", groups);
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
