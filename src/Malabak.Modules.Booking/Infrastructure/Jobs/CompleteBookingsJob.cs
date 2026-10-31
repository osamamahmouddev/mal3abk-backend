using Hangfire;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Infrastructure.Jobs;

/// <summary>
/// Marks confirmed bookings whose match has ended as completed (TASK-405). Each booking is saved on its own, then announced:
/// one that changed at that moment (the venue marking a no-show) is skipped and never holds up the others or leaves a false
/// "completed" announcement.
/// </summary>
public sealed class CompleteBookingsJob
{
    public const string RecurringJobId = "booking-complete-bookings-job";
    public const string CronSchedule = "*/5 * * * *"; // Every 5 minutes
    public const int BatchSize = 100;

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

    [Queue("default")]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task ExecuteRecurringSweepAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var endedIds = await _dbContext.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.Confirmed && b.EndUtc <= nowUtc && !b.IsDeleted)
            .OrderBy(b => b.EndUtc)
            .Select(b => b.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (endedIds.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Found {Count} confirmed bookings to auto-complete", endedIds.Count);

        foreach (var id in endedIds)
        {
            await CompleteAsync(id, nowUtc, cancellationToken);
        }
    }

    private async Task CompleteAsync(Guid bookingId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        var booking = await _dbContext.Bookings.Include(b => b.History)
            .FirstOrDefaultAsync(b => b.Id == bookingId && !b.IsDeleted, cancellationToken);
        if (booking is null || booking.Status != BookingStatus.Confirmed)
        {
            return;
        }

        var completed = booking.Complete(nowUtc);
        if (completed.IsFailure)
        {
            _logger.LogWarning("Booking {BookingId} could not be auto-completed: {Error}", booking.Id, completed.Error.Code);
            return;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogInformation(ex, "Booking {BookingId} changed while it was being completed; skipping it", booking.Id);
            return;
        }

        try
        {
            await _broadcaster.BroadcastBookingUpdatedAsync(
                booking.PlayerId, booking.VenueId, booking.Id, booking.BookingReference, BookingStatus.Completed.ToString(), nowUtc, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Booking {BookingId} was completed but the live update failed", booking.Id);
        }
    }
}
