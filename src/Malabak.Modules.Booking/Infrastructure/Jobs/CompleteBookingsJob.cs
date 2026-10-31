using Hangfire;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Infrastructure.Jobs;

public sealed class CompleteBookingsJob
{
    public const string RecurringJobId = "booking-complete-bookings-job";
    public const string CronSchedule = "*/5 * * * *"; // Every 5 minutes

    private readonly BookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IBookingRealtimeBroadcaster _broadcaster;
    private readonly ILogger<CompleteBookingsJob> _logger;

    public CompleteBookingsJob(
        BookingDbContext dbContext,
        IClock clock,
        IBookingRealtimeBroadcaster broadcaster,
        ILogger<CompleteBookingsJob> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task ExecuteRecurringSweepAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var confirmedBookingsPastEnd = await _dbContext.Bookings
            .Where(b => b.Status == BookingStatus.Confirmed
                     && b.EndUtc <= nowUtc
                     && !b.IsDeleted)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (confirmedBookingsPastEnd.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Found {Count} confirmed bookings to auto-complete", confirmedBookingsPastEnd.Count);

        foreach (var booking in confirmedBookingsPastEnd)
        {
            var completeResult = booking.Complete(nowUtc);
            if (completeResult.IsFailure)
            {
                _logger.LogWarning("Failed to auto-complete booking {BookingId}: {Error}", booking.Id, completeResult.Error);
                continue;
            }

            await _broadcaster.BroadcastBookingUpdatedAsync(
                booking.PlayerId,
                booking.VenueId,
                booking.Id,
                booking.BookingReference,
                BookingStatus.Completed.ToString(),
                nowUtc,
                cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
