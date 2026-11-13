using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Application.Bookings.CancelBooking;

public sealed class CancelBookingHandler : ICommandHandler<CancelBookingCommand, Result>
{
    /// <summary>Matches the column length of the cancellation reason (booking, refund and history).</summary>
    public const int MaxReasonLength = 500;

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

        // Someone else's booking looks like one that does not exist, so ids cannot be probed.
        if (booking.PlayerId != command.PlayerId)
        {
            return Result.Failure(BookingErrors.NotFound);
        }

        if (command.Reason is { Length: > MaxReasonLength })
        {
            return Result.Failure(Error.Validation("Booking.CancellationReasonTooLong", $"The cancellation reason cannot exceed {MaxReasonLength} characters."));
        }

        // Policy deadline check if booking is confirmed
        if (booking.Status == BookingStatus.Confirmed)
        {
            // Once the match has started nothing can be cancelled, whatever the policy window says.
            if (nowUtc >= booking.StartUtc)
            {
                return Result.Failure(BookingErrors.CannotCancelPastBooking);
            }

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

        // If deposit was submitted or confirmed, track expected refund
        if (oldStatus is BookingStatus.Confirmed or BookingStatus.ProofSubmitted or BookingStatus.UnderReview)
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

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The booking changed while this request was running (a review, an expiry or a second cancel).
            return Result.Failure(Error.Conflict("Booking.ConcurrentUpdate", "The booking was changed by another request. Please reload and try again."));
        }

        _logger.LogInformation("Cancelled booking {BookingId} ({Reference})", booking.Id, booking.BookingReference);

        // The cancellation is saved. Only now is the slot announced as free and the player's view updated; a failing side
        // effect is logged and never turned into an error that would make the player retry a cancellation that happened.
        await RunGuardedAsync(booking, "clear cached availability", () => _availabilityInvalidator.InvalidatePitchAsync(booking.PitchId, cancellationToken));
        await RunGuardedAsync(booking, "broadcast the freed slot", () => _broadcaster.BroadcastSlotStatusChangedAsync(
            booking.VenueId, booking.PitchId, booking.StartUtc, booking.EndUtc, "free", cancellationToken));
        await RunGuardedAsync(booking, "broadcast the booking update", () => _broadcaster.BroadcastBookingUpdatedAsync(
            booking.PlayerId, booking.VenueId, booking.Id, booking.BookingReference, BookingStatus.Cancelled.ToString(), nowUtc, cancellationToken));

        return Result.Success();
    }

    private async Task RunGuardedAsync(Domain.Booking booking, string step, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The cancellation of booking {BookingId} was saved but the step '{Step}' failed.", booking.Id, step);
        }
    }
}
