using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Application.Bookings.ReviewProof;

/// <summary>
/// Venue staff approve or reject a submitted payment proof (TASK-035). Approving confirms the booking; rejecting needs a
/// reason and, while the hold is running, lets the player send a corrected proof. The decision is saved first; only then do
/// the cache, live updates, notification and events run, so nobody is told about a decision that did not persist.
/// </summary>
public sealed class ReviewProofHandler : ICommandHandler<ReviewProofCommand, Result>
{
    /// <summary>Matches the column length of the rejection reason (booking, proof and history).</summary>
    public const int MaxRejectionReasonLength = 500;

    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IBookingRealtimeBroadcaster _broadcaster;
    private readonly IAvailabilityInvalidator _availabilityInvalidator;
    private readonly INotificationPublisher? _notificationPublisher;
    private readonly ILogger<ReviewProofHandler> _logger;

    public ReviewProofHandler(
        IBookingDbContext dbContext,
        IClock clock,
        IBookingRealtimeBroadcaster broadcaster,
        IAvailabilityInvalidator availabilityInvalidator,
        ILogger<ReviewProofHandler> logger,
        INotificationPublisher? notificationPublisher = null)
    {
        _dbContext = dbContext;
        _clock = clock;
        _broadcaster = broadcaster;
        _availabilityInvalidator = availabilityInvalidator;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(ReviewProofCommand command, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var actor = command.ActorUserId.ToString("D");

        var booking = await _dbContext.Bookings
            .Include(b => b.Proofs)
            .Include(b => b.History)
            .FirstOrDefaultAsync(b => b.Id == command.BookingId && !b.IsDeleted, cancellationToken);

        // Another venue's booking looks like one that does not exist, so ids cannot be probed.
        if (booking is null || booking.VenueId != command.VenueId)
        {
            return Result.Failure(BookingErrors.NotFound);
        }

        var reason = command.RejectionReason?.Trim();
        if (!command.Approved)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return Result.Failure(BookingErrors.RejectionReasonRequired);
            }

            if (reason.Length > MaxRejectionReasonLength)
            {
                return Result.Failure(Error.Validation("Booking.RejectionReasonTooLong", $"The rejection reason cannot exceed {MaxRejectionReasonLength} characters."));
            }
        }

        var decision = command.Approved
            ? booking.Confirm(actor, nowUtc)
            : booking.Reject(reason!, actor, nowUtc);
        if (decision.IsFailure)
        {
            return decision;
        }

        var latestProof = booking.Proofs.OrderByDescending(p => p.UploadedAtUtc).FirstOrDefault();
        latestProof?.Review(command.Approved, command.Approved ? null : reason, actor, nowUtc);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else decided, or the player changed the booking, while this request was running.
            return Result.Failure(Error.Conflict("Booking.ConcurrentUpdate", "The booking was changed by another request. Please reload and try again."));
        }

        await AfterCommitAsync(booking, command.Approved, reason, nowUtc, cancellationToken);
        return Result.Success();
    }

    /// <summary>The decision is saved: a failing side effect is logged, never turned into an error for the reviewer.</summary>
    private async Task AfterCommitAsync(Domain.Booking booking, bool approved, string? reason, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await RunGuardedAsync(booking, "clear cached availability", () => _availabilityInvalidator.InvalidatePitchAsync(booking.PitchId, cancellationToken));

        // A rejection frees the slot at once only when the hold has already run out; otherwise it stays held for a corrected proof.
        string? slotStatus = null;
        if (approved)
        {
            slotStatus = "booked";
        }
        else if (booking.HoldExpiresAtUtc <= nowUtc)
        {
            slotStatus = "free";
        }

        if (slotStatus is not null)
        {
            await RunGuardedAsync(booking, "broadcast the slot status", () => _broadcaster.BroadcastSlotStatusChangedAsync(
                booking.VenueId, booking.PitchId, booking.StartUtc, booking.EndUtc, slotStatus, cancellationToken));
        }

        var newStatus = approved ? BookingStatus.Confirmed : BookingStatus.Rejected;
        await RunGuardedAsync(booking, "broadcast the booking update", () => _broadcaster.BroadcastBookingUpdatedAsync(
            booking.PlayerId, booking.VenueId, booking.Id, booking.BookingReference, newStatus.ToString(), nowUtc, cancellationToken));

        if (_notificationPublisher is not null)
        {
            var notification = approved
                ? new NotificationRequest(
                    booking.PlayerId,
                    NotificationTypes.BookingConfirmed,
                    new Dictionary<string, string>
                    {
                        ["BookingReference"] = booking.BookingReference,
                        ["PitchId"] = booking.PitchId.ToString("D"),
                        ["StartUtc"] = booking.StartUtc.ToString("o"),
                    },
                    $"booking-confirmed:{booking.Id:D}")
                : new NotificationRequest(
                    booking.PlayerId,
                    NotificationTypes.BookingRejected,
                    new Dictionary<string, string>
                    {
                        ["BookingReference"] = booking.BookingReference,
                        ["RejectionReason"] = reason ?? string.Empty,
                        ["HoldExpiresAtUtc"] = booking.HoldExpiresAtUtc?.ToString("o") ?? string.Empty,
                    },
                    $"booking-rejected:{booking.Id:D}");

            await RunGuardedAsync(booking, "publish the notification", () => _notificationPublisher.PublishAsync(notification, cancellationToken));
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
            _logger.LogError(ex, "The review of booking {BookingId} was saved but the step '{Step}' failed.", booking.Id, step);
        }
    }
}
