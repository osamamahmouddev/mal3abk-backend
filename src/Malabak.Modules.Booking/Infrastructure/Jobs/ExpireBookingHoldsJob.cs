using Hangfire;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Infrastructure.Jobs;

public sealed class ExpireBookingHoldsJob
{
    public const string RecurringJobId = "booking-expire-holds-job";
    public const string CronSchedule = "*/1 * * * *"; // Every 60 seconds

    private readonly BookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IAvailabilityInvalidator _availabilityInvalidator;
    private readonly IBookingRealtimeBroadcaster _broadcaster;
    private readonly INotificationPublisher? _notificationPublisher;
    private readonly ILogger<ExpireBookingHoldsJob> _logger;

    public ExpireBookingHoldsJob(
        BookingDbContext dbContext,
        IClock clock,
        IAvailabilityInvalidator availabilityInvalidator,
        IBookingRealtimeBroadcaster broadcaster,
        ILogger<ExpireBookingHoldsJob> logger,
        INotificationPublisher? notificationPublisher = null)
    {
        _dbContext = dbContext;
        _clock = clock;
        _availabilityInvalidator = availabilityInvalidator;
        _broadcaster = broadcaster;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 30)]
    public async Task ExecuteRecurringSweepAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var expiredBookings = await _dbContext.Bookings
            .Where(b => b.Status == BookingStatus.PendingDeposit
                     && b.HoldExpiresAtUtc <= nowUtc
                     && !b.IsDeleted)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (expiredBookings.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Found {Count} expired booking holds in recurring sweep", expiredBookings.Count);

        foreach (var booking in expiredBookings)
        {
            await ExpireSingleBookingInternalAsync(booking, nowUtc, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ExecuteForBookingAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var booking = await _dbContext.Bookings
            .FirstOrDefaultAsync(b => b.Id == bookingId && !b.IsDeleted, cancellationToken);

        if (booking is null || booking.Status != BookingStatus.PendingDeposit)
        {
            return;
        }

        if (booking.HoldExpiresAtUtc > nowUtc)
        {
            // Hold hasn't actually expired yet (e.g. extension or clock jitter)
            return;
        }

        await ExpireSingleBookingInternalAsync(booking, nowUtc, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Expired booking {BookingId} ({Reference}) via specific delayed job", booking.Id, booking.BookingReference);
    }

    private async Task ExpireSingleBookingInternalAsync(Domain.Booking booking, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var expireResult = booking.Expire(nowUtc);
        if (expireResult.IsFailure)
        {
            _logger.LogWarning("Failed to transition booking {BookingId} to Expired: {Error}", booking.Id, expireResult.Error);
            return;
        }

        // Invalidate pitch availability cache
        await _availabilityInvalidator.InvalidatePitchAsync(booking.PitchId, cancellationToken);

        // Broadcast slot released to SignalR public venue group
        await _broadcaster.BroadcastSlotStatusChangedAsync(
            booking.VenueId,
            booking.PitchId,
            booking.StartUtc,
            booking.EndUtc,
            "free",
            cancellationToken);

        // Broadcast booking updated to player and staff
        await _broadcaster.BroadcastBookingUpdatedAsync(
            booking.PlayerId,
            booking.VenueId,
            booking.Id,
            booking.BookingReference,
            BookingStatus.Expired.ToString(),
            nowUtc,
            cancellationToken);

        // Dispatch notification
        if (_notificationPublisher is not null)
        {
            var request = new NotificationRequest(
                booking.PlayerId,
                NotificationTypes.BookingExpired,
                new Dictionary<string, string>
                {
                    ["BookingReference"] = booking.BookingReference,
                    ["PitchId"] = booking.PitchId.ToString("D"),
                    ["StartUtc"] = booking.StartUtc.ToString("o"),
                    ["HoldDurationMinutes"] = "15"
                },
                $"booking-expired:{booking.Id:D}");

            try
            {
                await _notificationPublisher.PublishAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish notification for expired booking {BookingId}", booking.Id);
            }
        }
    }
}
