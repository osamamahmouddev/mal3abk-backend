using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Application.Bookings.PaymentDetails;

public sealed class BookingPaymentConfirmationService : IBookingPaymentConfirmationContract, IBookingPaymentDetailsProvider
{
    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IBookingRealtimeBroadcaster _broadcaster;
    private readonly IAvailabilityInvalidator _availabilityInvalidator;
    private readonly INotificationPublisher? _notifications;
    private readonly ILogger<BookingPaymentConfirmationService> _logger;

    public BookingPaymentConfirmationService(
        IBookingDbContext dbContext,
        IClock clock,
        IBookingRealtimeBroadcaster broadcaster,
        IAvailabilityInvalidator availabilityInvalidator,
        ILogger<BookingPaymentConfirmationService> logger,
        INotificationPublisher? notifications = null)
    {
        _dbContext = dbContext;
        _clock = clock;
        _broadcaster = broadcaster;
        _availabilityInvalidator = availabilityInvalidator;
        _logger = logger;
        _notifications = notifications;
    }

    public async Task<Result<PayableBookingDetails>> GetPayableBookingAsync(Guid bookingId, Guid playerId, CancellationToken cancellationToken = default)
    {
        var booking = await _dbContext.Bookings.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == bookingId && !b.IsDeleted, cancellationToken);

        if (booking is null)
        {
            return Result.Failure<PayableBookingDetails>(Error.NotFound("Booking.NotFound", $"Booking {bookingId} was not found."));
        }

        if (booking.PlayerId != playerId)
        {
            return Result.Failure<PayableBookingDetails>(Error.NotFound("Booking.NotFound", $"Booking {bookingId} was not found."));
        }

        return Result.Success(new PayableBookingDetails(
            booking.Id,
            booking.PlayerId,
            booking.BookingReference,
            booking.PlatformCommission.Amount,
            booking.TotalPrice.Currency,
            booking.Status.ToString(),
            booking.IsCommissionPaid));
    }

    public async Task<Result> ConfirmCommissionPaymentAsync(ConfirmBookingCommissionRequest request, CancellationToken cancellationToken = default)
    {
        var booking = await _dbContext.Bookings
            .FirstOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken);

        if (booking is null)
        {
            return Result.Failure(Error.NotFound("Booking.NotFound", $"Booking {request.BookingId} was not found."));
        }

        if (booking.IsCommissionPaid && booking.Status == BookingStatus.Confirmed)
        {
            _logger.LogInformation("Booking {BookingId} is already marked as commission paid.", request.BookingId);
            return Result.Success();
        }

        var money = new Money(request.AmountPaid, request.Currency);
        var nowUtc = _clock.UtcNow;
        var confirmResult = booking.ConfirmOnlinePayment(
            request.ProviderOrderId,
            request.ProviderTransactionId,
            money,
            nowUtc,
            "PaymobWebhook");

        if (confirmResult.IsFailure)
        {
            _logger.LogWarning("Failed to confirm booking {BookingId} online payment: {Error}", request.BookingId, confirmResult.Error);
            return confirmResult;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogError(ex, "Concurrency collision when confirming online payment for booking {BookingId}", request.BookingId);
            return Result.Failure(Error.Conflict("Booking.ConcurrentUpdate", "The booking was updated concurrently."));
        }

        // Side-effects
        try
        {
            await _availabilityInvalidator.InvalidatePitchAsync(booking.PitchId, cancellationToken);
            await _broadcaster.BroadcastSlotStatusChangedAsync(
                booking.VenueId, booking.PitchId, booking.StartUtc, booking.EndUtc, "booked", cancellationToken);
            await _broadcaster.BroadcastBookingUpdatedAsync(
                booking.PlayerId, booking.VenueId, booking.Id, booking.BookingReference, "Confirmed", nowUtc, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast confirmation for booking {BookingId}", booking.Id);
        }

        if (_notifications is not null)
        {
            try
            {
                var data = new Dictionary<string, string>
                {
                    ["bookingId"] = booking.Id.ToString(),
                    ["bookingReference"] = booking.BookingReference
                };
                await _notifications.PublishAsync(new NotificationRequest(
                    booking.PlayerId,
                    NotificationTypes.BookingConfirmed,
                    data,
                    $"booking-confirmed:{booking.Id}"), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send push notification for confirmed booking {BookingId}", booking.Id);
            }
        }

        return Result.Success();
    }
}
