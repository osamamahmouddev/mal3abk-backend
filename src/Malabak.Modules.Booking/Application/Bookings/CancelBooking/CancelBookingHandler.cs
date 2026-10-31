using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Application.Bookings.CancelBooking;

public sealed class CancelBookingHandler : ICommandHandler<CancelBookingCommand, Result>
{
    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IAvailabilityInvalidator _availabilityInvalidator;
    private readonly IBookingRealtimeBroadcaster _broadcaster;
    private readonly ILogger<CancelBookingHandler> _logger;

    public CancelBookingHandler(
        IBookingDbContext dbContext,
        IClock clock,
        IAvailabilityInvalidator availabilityInvalidator,
        IBookingRealtimeBroadcaster broadcaster,
        ILogger<CancelBookingHandler> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _availabilityInvalidator = availabilityInvalidator;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(CancelBookingCommand command, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var booking = await _dbContext.Bookings
            .Include(b => b.History)
            .FirstOrDefaultAsync(b => b.Id == command.BookingId && !b.IsDeleted, cancellationToken);

        if (booking is null)
        {
            return Result.Failure(BookingErrors.NotFound);
        }

        // Ownership enforcement
        if (booking.PlayerId != command.PlayerId)
        {
            return Result.Failure(Error.Forbidden("Booking.Forbidden", "You do not own this booking."));
        }

        // Policy deadline check if booking is confirmed
        if (booking.Status == BookingStatus.Confirmed)
        {
            var leadHours = booking.PolicySnapshot?.CancellationDeadlineHours ?? 24;
            var deadlineUtc = booking.StartUtc.AddHours(-leadHours);

            if (nowUtc > deadlineUtc)
            {
                return Result.Failure(BookingErrors.CancellationWindowClosed);
            }
        }

        var oldStatus = booking.Status;
        var cancelResult = booking.Cancel(command.Reason ?? string.Empty, command.PlayerId.ToString("D"), false, nowUtc, booking.PolicySnapshot?.CancellationDeadlineHours);
        if (cancelResult.IsFailure)
        {
            return cancelResult;
        }

        // Release slot lock
        await _availabilityInvalidator.InvalidatePitchAsync(booking.PitchId, cancellationToken);

        await _broadcaster.BroadcastSlotStatusChangedAsync(
            booking.VenueId,
            booking.PitchId,
            booking.StartUtc,
            booking.EndUtc,
            "free",
            cancellationToken);

        await _broadcaster.BroadcastBookingUpdatedAsync(
            booking.PlayerId,
            booking.VenueId,
            booking.Id,
            booking.BookingReference,
            BookingStatus.Cancelled.ToString(),
            nowUtc,
            cancellationToken);

        // If deposit was submitted or confirmed, track expected refund
        if (oldStatus is BookingStatus.Confirmed or BookingStatus.ProofSubmitted)
        {
            var refundPercentage = booking.PolicySnapshot?.RefundPercentage ?? 100;
            var refundAmount = booking.DepositAmount.Amount * (refundPercentage / 100m);

            var refund = BookingRefund.CreatePending(
                Guid.NewGuid(),
                booking.Id,
                refundAmount,
                booking.DepositAmount.Currency,
                command.Reason,
                booking.PolicySnapshot?.Terms,
                nowUtc);

            _dbContext.BookingRefunds.Add(refund);
        }

        _logger.LogInformation("Cancelled booking {BookingId} ({Reference})", booking.Id, booking.BookingReference);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
