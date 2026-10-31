using Hangfire;
using Malabak.Modules.Booking.Application;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Infrastructure.Jobs;

public sealed class HangfireHoldExpiryScheduler : IHoldExpiryScheduler
{
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<HangfireHoldExpiryScheduler> _logger;

    public HangfireHoldExpiryScheduler(
        IBackgroundJobClient backgroundJobClient,
        ILogger<HangfireHoldExpiryScheduler> logger)
    {
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    public void ScheduleHoldExpiry(Guid bookingId, DateTime holdExpiresAtUtc)
    {
        var delay = holdExpiresAtUtc - DateTime.UtcNow;
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        try
        {
            _backgroundJobClient.Schedule<ExpireBookingHoldsJob>(
                job => job.ExecuteForBookingAsync(bookingId, CancellationToken.None),
                delay);

            _logger.LogInformation("Scheduled Hangfire hold expiry job for booking {BookingId} with delay {Delay}",
                bookingId, delay);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to schedule Hangfire hold expiry for booking {BookingId}", bookingId);
        }
    }
}
