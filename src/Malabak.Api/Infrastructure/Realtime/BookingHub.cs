using System.Security.Claims;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Venues.Application.Common;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Malabak.Api.Infrastructure.Realtime;

public sealed record SlotStatusChangedNotification(
    Guid PitchId,
    DateTime StartUtc,
    DateTime EndUtc,
    string Status);

public sealed record BookingUpdatedNotification(
    Guid BookingId,
    string Reference,
    Guid VenueId,
    string Status,
    DateTime UpdatedAtUtc);

public interface IBookingHubClient
{
    Task SlotStatusChanged(SlotStatusChangedNotification notification);
    Task BookingUpdated(BookingUpdatedNotification notification);
}

[Authorize(AuthenticationSchemes = RealtimeAuthenticationExtensions.WsTicketScheme)]
public sealed class BookingHub : Hub<IBookingHubClient>
{
    private readonly IVenueAccessService _venueAccessService;
    private readonly ILogger<BookingHub> _logger;

    public BookingHub(IVenueAccessService venueAccessService, ILogger<BookingHub> logger)
    {
        _venueAccessService = venueAccessService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.GetUserId();
        if (userId.HasValue)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroupName(userId.Value));
            _logger.LogInformation("SignalR client {ConnectionId} bound to user group {UserId}", Context.ConnectionId, userId.Value);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Subscribe to live slot availability changes for a specific venue and date.
    /// Available to any authenticated player/guest. Broadcasts contain zero PII.
    /// </summary>
    public async Task WatchVenueAsync(Guid venueId, string dateIso)
    {
        if (venueId == Guid.Empty || !DateOnly.TryParse(dateIso, System.Globalization.CultureInfo.InvariantCulture, out var date))
        {
            throw new HubException("Invalid venueId or date format (expected YYYY-MM-DD).");
        }

        var groupName = VenueDateGroupName(venueId, date);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogDebug("Client {ConnectionId} watching venue {GroupName}", Context.ConnectionId, groupName);
    }

    public async Task LeaveVenueAsync(Guid venueId, string dateIso)
    {
        if (venueId == Guid.Empty || !DateOnly.TryParse(dateIso, System.Globalization.CultureInfo.InvariantCulture, out var date))
        {
            return;
        }

        var groupName = VenueDateGroupName(venueId, date);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Join venue staff live notifications room. Server-side authorized from DB:
    /// Caller must be owner or active staff member of the venue.
    /// </summary>
    public async Task JoinStaffRoomAsync(Guid venueId)
    {
        var userId = Context.User?.GetUserId();
        if (!userId.HasValue || venueId == Guid.Empty)
        {
            throw new HubException("Unauthorized: Valid user session required.");
        }

        var access = await _venueAccessService.GetAccessAsync(userId.Value, venueId, Context.ConnectionAborted);
        if (!access.HasAccess || !access.Permissions.Contains(Permissions.BookingsView))
        {
            _logger.LogWarning("Forbidden SignalR staff room join attempt by user {UserId} on venue {VenueId}", userId.Value, venueId);
            throw new HubException("Forbidden: Not staff of this venue.");
        }

        var groupName = VenueStaffGroupName(venueId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Staff user {UserId} joined SignalR staff room {GroupName}", userId.Value, groupName);
    }

    public async Task LeaveStaffRoomAsync(Guid venueId)
    {
        if (venueId == Guid.Empty)
        {
            return;
        }

        var groupName = VenueStaffGroupName(venueId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }

    public static string UserGroupName(Guid userId) => $"user:{userId:D}";
    public static string VenueDateGroupName(Guid venueId, DateOnly date) => $"venue:{venueId:D}:{date:yyyy-MM-dd}";
    public static string VenueStaffGroupName(Guid venueId) => $"venue-staff:{venueId:D}";
}
