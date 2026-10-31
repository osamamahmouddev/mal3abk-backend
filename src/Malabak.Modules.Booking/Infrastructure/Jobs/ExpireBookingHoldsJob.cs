using System.Globalization;
using Hangfire;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Infrastructure.Jobs;

/// <summary>
/// Frees the slot of every booking whose hold ran out without a deposit proof (TASK-036, ADR-0008). A recurring sweep every
/// minute is the safety net; a delayed job per booking makes the release prompt. Both are idempotent: they only act on a
/// booking that is still waiting for its deposit and whose hold has lapsed, so a proof submitted in the meantime, a duplicate
/// run or a retry can never override anything.
/// </summary>
public sealed class ExpireBookingHoldsJob
{
    public const string RecurringJobId = "booking-expire-holds-job";
    public const string CronSchedule = "*/1 * * * *"; // Every 60 seconds
    public const int SweepBatchSize = 50;

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

    [Queue("critical")]
    [DisableConcurrentExecution(timeoutInSeconds: 30)]
    public async Task ExecuteRecurringSweepAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        // Oldest holds first, so a backlog is worked off in order; the rest is picked up on the next run.
        var expiredIds = await _dbContext.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.PendingDeposit && b.HoldExpiresAtUtc <= nowUtc && !b.IsDeleted)
            .OrderBy(b => b.HoldExpiresAtUtc)
            .Select(b => b.Id)
            .Take(SweepBatchSize)
            .ToListAsync(cancellationToken);

        if (expiredIds.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Found {Count} expired booking holds in recurring sweep", expiredIds.Count);

        var expired = 0;
        foreach (var id in expiredIds)
        {
            if (await ExpireAsync(id, nowUtc, cancellationToken))
            {
                expired++;
            }
        }

        _logger.LogInformation("Expired {Expired} of {Found} booking holds", expired, expiredIds.Count);
    }

    [Queue("critical")]
    public async Task ExecuteForBookingAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        if (await ExpireAsync(bookingId, _clock.UtcNow, cancellationToken))
        {
            _logger.LogInformation("Expired booking {BookingId} via its scheduled job", bookingId);
        }
    }

    /// <summary>
    /// Expires one booking on its own: it is loaded fresh, saved, and only then announced. One booking that changed under the
    /// job (a proof arriving at that moment) is skipped and never holds up the others or leaves a false announcement.
    /// </summary>
    private async Task<bool> ExpireAsync(Guid bookingId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        var booking = await _dbContext.Bookings.Include(b => b.History)
            .FirstOrDefaultAsync(b => b.Id == bookingId && !b.IsDeleted, cancellationToken);

        // Missing, already past the deposit stage, or the hold is still running (an extension, clock jitter): nothing to do.
        if (booking is null || booking.Status != BookingStatus.PendingDeposit || booking.HoldExpiresAtUtc > nowUtc)
        {
            return false;
        }

        var holdMinutes = (int)Math.Round(((booking.HoldExpiresAtUtc ?? nowUtc) - booking.CreatedAtUtc).TotalMinutes);

        var expired = booking.Expire(nowUtc);
        if (expired.IsFailure)
        {
            _logger.LogWarning("Booking {BookingId} could not be expired: {Error}", booking.Id, expired.Error.Code);
            return false;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogInformation(ex, "Booking {BookingId} changed while its hold was being expired; skipping it", booking.Id);
            return false;
        }

        await AnnounceAsync(booking, nowUtc, holdMinutes, cancellationToken);
        return true;
    }

    /// <summary>The booking is expired and saved. A failing side effect is logged and does not stop the next booking.</summary>
    private async Task AnnounceAsync(Domain.Booking booking, DateTime nowUtc, int holdMinutes, CancellationToken cancellationToken)
    {
        await RunGuardedAsync(booking, "clear cached availability", () => _availabilityInvalidator.InvalidatePitchAsync(booking.PitchId, cancellationToken));
        await RunGuardedAsync(booking, "broadcast the freed slot", () => _broadcaster.BroadcastSlotStatusChangedAsync(
            booking.VenueId, booking.PitchId, booking.StartUtc, booking.EndUtc, "free", cancellationToken));
        await RunGuardedAsync(booking, "broadcast the booking update", () => _broadcaster.BroadcastBookingUpdatedAsync(
            booking.PlayerId, booking.VenueId, booking.Id, booking.BookingReference, BookingStatus.Expired.ToString(), nowUtc, cancellationToken));

        if (_notificationPublisher is not null)
        {
            var request = new NotificationRequest(
                booking.PlayerId,
                NotificationTypes.BookingExpired,
                new Dictionary<string, string>
                {
                    ["BookingReference"] = booking.BookingReference,
                    ["PitchId"] = booking.PitchId.ToString("D"),
                    ["StartUtc"] = booking.StartUtc.ToString("o", CultureInfo.InvariantCulture),
                    ["HoldDurationMinutes"] = holdMinutes.ToString(CultureInfo.InvariantCulture),
                },
                $"booking-expired:{booking.Id:D}");

            await RunGuardedAsync(booking, "publish the notification", () => _notificationPublisher.PublishAsync(request, cancellationToken));
        }
    }

    private async Task RunGuardedAsync(Domain.Booking booking, string step, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Booking {BookingId} was expired but the step '{Step}' failed.", booking.Id, step);
        }
    }
}
