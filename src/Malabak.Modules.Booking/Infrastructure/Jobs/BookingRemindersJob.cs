using Hangfire;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Infrastructure.Jobs;

/// <summary>
/// Reminds players 24 hours and 2 hours before a confirmed booking. Every booking in a window is reached (in pages, so a busy
/// evening is not cut off); each reminder carries a dedupe key, so a booking is never reminded twice by overlapping runs.
/// </summary>
public sealed class BookingRemindersJob
{
    public const string RecurringJobId = "booking-reminders-job";
    public const string CronSchedule = "*/15 * * * *"; // Every 15 minutes
    public const int PageSize = 200;

    private readonly BookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IVenueDirectory _venues;
    private readonly INotificationPublisher? _notificationPublisher;
    private readonly ILogger<BookingRemindersJob> _logger;

    public BookingRemindersJob(
        BookingDbContext dbContext,
        IClock clock,
        IVenueDirectory venues,
        ILogger<BookingRemindersJob> logger,
        INotificationPublisher? notificationPublisher = null)
    {
        _dbContext = dbContext;
        _clock = clock;
        _venues = venues;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task ExecuteRecurringSweepAsync(CancellationToken cancellationToken = default)
    {
        if (_notificationPublisher is null)
        {
            return;
        }

        var nowUtc = _clock.UtcNow;

        // Windows are 30 minutes wide around the mark and the job runs every 15, so a booking is always caught at least once.
        await RemindAsync(nowUtc.AddHours(23).AddMinutes(45), nowUtc.AddHours(24).AddMinutes(15), BookingNotifications.Reminder24h, "24h", cancellationToken);
        await RemindAsync(nowUtc.AddHours(1).AddMinutes(45), nowUtc.AddHours(2).AddMinutes(15), BookingNotifications.Reminder2h, "2h", cancellationToken);
    }

    private async Task RemindAsync(
        DateTime from,
        DateTime to,
        Func<Domain.Booking, string, IClock, NotificationRequest> build,
        string label,
        CancellationToken cancellationToken)
    {
        var lastId = Guid.Empty;
        while (true)
        {
            var page = await _dbContext.Bookings.AsNoTracking()
                .Where(b => b.Status == BookingStatus.Confirmed && b.StartUtc >= from && b.StartUtc <= to && !b.IsDeleted && b.Id.CompareTo(lastId) > 0)
                .OrderBy(b => b.Id)
                .Take(PageSize)
                .ToListAsync(cancellationToken);
            if (page.Count == 0)
            {
                return;
            }

            lastId = page[^1].Id;
            var names = await _venues.GetVenueNamesAsync(page.Select(b => b.VenueId).Distinct().ToList(), cancellationToken);

            foreach (var booking in page)
            {
                try
                {
                    await _notificationPublisher!.PublishAsync(build(booking, names.GetValueOrDefault(booking.VenueId, string.Empty), _clock), cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to publish the {Window} reminder for booking {BookingId}", label, booking.Id);
                }
            }
        }
    }
}
