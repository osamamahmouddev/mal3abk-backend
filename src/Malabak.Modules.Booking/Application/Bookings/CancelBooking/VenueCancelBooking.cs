using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Application.Bookings.CancelBooking;

/// <summary>Venue staff cancel a booking (maintenance, duplicate, ...). The reason is required and shown to the player.</summary>
public sealed record VenueCancelBookingCommand(Guid BookingId, Guid VenueId, Guid ActorUserId, string Reason) : ICommand<Result>;

/// <summary>
/// Staff cancellation (TASK-502). Unlike a player cancellation there is no deadline: the venue may cancel even close to the
/// start. Because the venue cancelled, any deposit the player paid or uploaded is owed back in full, recorded as a pending
/// refund the staff confirm after transferring it. Bookings of other venues look like they do not exist.
/// </summary>
public sealed class VenueCancelBookingHandler : ICommandHandler<VenueCancelBookingCommand, Result>
{
    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IAvailabilityInvalidator _availabilityInvalidator;
    private readonly IBookingRealtimeBroadcaster _broadcaster;
    private readonly ILogger<VenueCancelBookingHandler> _logger;

    public VenueCancelBookingHandler(
        IBookingDbContext dbContext,
        IClock clock,
        IAvailabilityInvalidator availabilityInvalidator,
        IBookingRealtimeBroadcaster broadcaster,
        ILogger<VenueCancelBookingHandler> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _availabilityInvalidator = availabilityInvalidator;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(VenueCancelBookingCommand command, CancellationToken cancellationToken = default)
    {
        var reason = command.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            return Result.Failure(BookingErrors.ReasonRequired);
        }

        if (reason.Length > CancelBookingHandler.MaxReasonLength)
        {
            return Result.Failure(Error.Validation("Booking.CancellationReasonTooLong", $"The cancellation reason cannot exceed {CancelBookingHandler.MaxReasonLength} characters."));
        }

        var booking = await _dbContext.Bookings
            .Include(b => b.History)
            .FirstOrDefaultAsync(b => b.Id == command.BookingId && b.VenueId == command.VenueId && !b.IsDeleted, cancellationToken);
        if (booking is null)
        {
            return Result.Failure(BookingErrors.NotFound);
        }

        var nowUtc = _clock.UtcNow;
        var oldStatus = booking.Status;
        var cancelled = booking.Cancel(reason, command.ActorUserId.ToString("D"), isStaff: true, nowUtc);
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        if (!booking.IsManualBooking && booking.DepositAmount.Amount > 0
            && oldStatus is BookingStatus.Confirmed or BookingStatus.ProofSubmitted or BookingStatus.UnderReview)
        {
            _dbContext.BookingRefunds.Add(BookingRefund.CreatePending(
                Guid.NewGuid(), booking.Id, booking.DepositAmount.Amount, booking.DepositAmount.Currency, reason, "Cancelled by the venue: full deposit refund.", nowUtc));
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(Error.Conflict("Booking.ConcurrentUpdate", "The booking was changed by another request. Please reload and try again."));
        }

        _logger.LogInformation("Venue staff cancelled booking {BookingId} ({Reference})", booking.Id, booking.BookingReference);

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
            _logger.LogError(ex, "The staff cancellation of booking {BookingId} was saved but the step '{Step}' failed.", booking.Id, step);
        }
    }
}
