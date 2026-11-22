using Hangfire;
using Malabak.Modules.Booking.Application;
using Malabak.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Infrastructure.Jobs;

public sealed class HangfireHoldExpiryScheduler : IHoldExpiryScheduler
{
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IClock _clock;
    private readonly ILogger<HangfireHoldExpiryScheduler> _logger;

    public HangfireHoldExpiryScheduler(
        IBackgroundJobClient backgroundJobClient,
        IClock clock,
        ILogger<HangfireHoldExpiryScheduler> logger)
    {
        _backgroundJobClient = backgroundJobClient;
        _clock = clock;
        _logger = logger;
    }

    public void ScheduleHoldExpiry(Guid bookingId, DateTime holdExpiresAtUtc)
    {
        var delay = holdExpiresAtUtc - _clock.UtcNow;
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
