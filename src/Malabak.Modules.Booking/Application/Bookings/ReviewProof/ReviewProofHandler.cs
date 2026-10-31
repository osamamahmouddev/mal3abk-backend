using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Application.Bookings.ReviewProof;

public sealed class ReviewProofHandler : ICommandHandler<ReviewProofCommand, Result>
{
    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IBookingRealtimeBroadcaster _broadcaster;
    private readonly IAvailabilityInvalidator _availabilityInvalidator;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly INotificationPublisher? _notificationPublisher;
    private readonly ILogger<ReviewProofHandler> _logger;

    public ReviewProofHandler(
        IBookingDbContext dbContext,
        IClock clock,
        IBookingRealtimeBroadcaster broadcaster,
        IAvailabilityInvalidator availabilityInvalidator,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<ReviewProofHandler> logger,
        INotificationPublisher? notificationPublisher = null)
    {
        _dbContext = dbContext;
        _clock = clock;
        _broadcaster = broadcaster;
        _availabilityInvalidator = availabilityInvalidator;
        _domainEventDispatcher = domainEventDispatcher;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(ReviewProofCommand command, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var booking = await _dbContext.Bookings
            .Include(b => b.Proofs)
            .Include(b => b.History)
            .FirstOrDefaultAsync(b => b.Id == command.BookingId && !b.IsDeleted, cancellationToken);

        if (booking is null)
        {
            return Result.Failure(BookingErrors.NotFound);
        }

        if (booking.VenueId != command.VenueId)
        {
            return Result.Failure(Error.Forbidden("Booking.Forbidden", "Booking does not belong to the specified venue."));
        }

        Result reviewResult;

        if (command.Approved)
        {
            reviewResult = booking.Confirm(command.ActorUserId.ToString("D"), nowUtc);
            if (reviewResult.IsFailure)
            {
                return reviewResult;
            }

            // Mark latest proof approved
            var latestProof = booking.Proofs.OrderByDescending(p => p.UploadedAtUtc).FirstOrDefault();
            latestProof?.Review(true, null, command.ActorUserId.ToString("D"), nowUtc);

            await _broadcaster.BroadcastSlotStatusChangedAsync(
                booking.VenueId,
                booking.PitchId,
                booking.StartUtc,
                booking.EndUtc,
                "booked",
                cancellationToken);

            await _broadcaster.BroadcastBookingUpdatedAsync(
                booking.PlayerId,
                booking.VenueId,
                booking.Id,
                booking.BookingReference,
                BookingStatus.Confirmed.ToString(),
                nowUtc,
                cancellationToken);

            if (_notificationPublisher is not null)
            {
                var notif = new NotificationRequest(
                    booking.PlayerId,
                    NotificationTypes.BookingConfirmed,
                    new Dictionary<string, string>
                    {
                        ["BookingReference"] = booking.BookingReference,
                        ["PitchId"] = booking.PitchId.ToString("D"),
                        ["StartUtc"] = booking.StartUtc.ToString("o")
                    },
                    $"booking-confirmed:{booking.Id:D}");

                try
                {
                    await _notificationPublisher.PublishAsync(notif, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to publish booking confirmed notification for booking {BookingId}", booking.Id);
                }
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(command.RejectionReason))
            {
                return Result.Failure(BookingErrors.RejectionReasonRequired);
            }

            reviewResult = booking.Reject(command.ActorUserId.ToString("D"), command.RejectionReason, nowUtc);
            if (reviewResult.IsFailure)
            {
                return reviewResult;
            }

            // Mark latest proof rejected
            var latestProof = booking.Proofs.OrderByDescending(p => p.UploadedAtUtc).FirstOrDefault();
            latestProof?.Review(false, command.RejectionReason, command.ActorUserId.ToString("D"), nowUtc);

            // If hold has already expired, slot is freed
            if (booking.HoldExpiresAtUtc <= nowUtc)
            {
                await _broadcaster.BroadcastSlotStatusChangedAsync(
                    booking.VenueId,
                    booking.PitchId,
                    booking.StartUtc,
                    booking.EndUtc,
                    "free",
                    cancellationToken);
            }

            await _broadcaster.BroadcastBookingUpdatedAsync(
                booking.PlayerId,
                booking.VenueId,
                booking.Id,
                booking.BookingReference,
                BookingStatus.Rejected.ToString(),
                nowUtc,
                cancellationToken);

            if (_notificationPublisher is not null)
            {
                var notif = new NotificationRequest(
                    booking.PlayerId,
                    NotificationTypes.BookingRejected,
                    new Dictionary<string, string>
                    {
                        ["BookingReference"] = booking.BookingReference,
                        ["RejectionReason"] = command.RejectionReason,
                        ["HoldExpiresAtUtc"] = booking.HoldExpiresAtUtc?.ToString("o") ?? string.Empty
                    },
                    $"booking-rejected:{booking.Id:D}");

                try
                {
                    await _notificationPublisher.PublishAsync(notif, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to publish booking rejected notification for booking {BookingId}", booking.Id);
                }
            }
        }

        await _availabilityInvalidator.InvalidatePitchAsync(booking.PitchId, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _domainEventDispatcher.DispatchAsync(booking.DomainEvents, cancellationToken);

        return Result.Success();
    }
}
