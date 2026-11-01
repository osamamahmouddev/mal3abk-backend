using Hangfire;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Infrastructure.Jobs;

public sealed class BookingRemindersJob
{
    public const string RecurringJobId = "booking-reminders-job";
    public const string CronSchedule = "*/15 * * * *"; // Every 15 minutes

    private readonly BookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly INotificationPublisher? _notificationPublisher;
    private readonly ILogger<BookingRemindersJob> _logger;

    public BookingRemindersJob(
        BookingDbContext dbContext,
        IClock clock,
        ILogger<BookingRemindersJob> logger,
        INotificationPublisher? notificationPublisher = null)
    {
        _dbContext = dbContext;
        _clock = clock;
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

        // 1. 24-Hour Reminders (StartUtc in [now + 23h45m, now + 24h15m])
        var window24hStart = nowUtc.AddHours(23).AddMinutes(45);
        var window24hEnd = nowUtc.AddHours(24).AddMinutes(15);

        var bookings24h = await _dbContext.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.Confirmed
                     && b.StartUtc >= window24hStart
                     && b.StartUtc <= window24hEnd
                     && !b.IsDeleted)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var b in bookings24h)
        {
            var req = new NotificationRequest(
                b.PlayerId,
                "booking.reminder_24h",
                new Dictionary<string, string>
                {
                    ["BookingReference"] = b.BookingReference,
                    ["StartUtc"] = b.StartUtc.ToString("o"),
                    ["PitchId"] = b.PitchId.ToString("D")
                },
                $"booking-reminder-24h:{b.Id:D}");

            try
            {
                await _notificationPublisher.PublishAsync(req, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish 24h reminder for booking {BookingId}", b.Id);
            }
        }

        // 2. 2-Hour Reminders (StartUtc in [now + 1h45m, now + 2h15m])
        var window2hStart = nowUtc.AddHours(1).AddMinutes(45);
        var window2hEnd = nowUtc.AddHours(2).AddMinutes(15);

        var bookings2h = await _dbContext.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.Confirmed
                     && b.StartUtc >= window2hStart
                     && b.StartUtc <= window2hEnd
                     && !b.IsDeleted)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var b in bookings2h)
        {
            var req = new NotificationRequest(
                b.PlayerId,
                "booking.reminder_2h",
                new Dictionary<string, string>
                {
                    ["BookingReference"] = b.BookingReference,
                    ["StartUtc"] = b.StartUtc.ToString("o"),
                    ["PitchId"] = b.PitchId.ToString("D")
                },
                $"booking-reminder-2h:{b.Id:D}");

            try
            {
                await _notificationPublisher.PublishAsync(req, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish 2h reminder for booking {BookingId}", b.Id);
            }
        }
    }
}
