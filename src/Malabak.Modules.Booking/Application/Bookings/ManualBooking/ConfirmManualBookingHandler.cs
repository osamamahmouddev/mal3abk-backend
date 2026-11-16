using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Application.Bookings.ManualBooking;

public sealed class ConfirmManualBookingHandler : ICommandHandler<ConfirmManualBookingCommand, Result>
{
    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IBookingRealtimeBroadcaster _broadcaster;
    private readonly IAvailabilityInvalidator _availabilityInvalidator;
    private readonly ILogger<ConfirmManualBookingHandler> _logger;

    public ConfirmManualBookingHandler(
        IBookingDbContext dbContext,
        IClock clock,
        IBookingRealtimeBroadcaster broadcaster,
        IAvailabilityInvalidator availabilityInvalidator,
        ILogger<ConfirmManualBookingHandler> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _broadcaster = broadcaster;
        _availabilityInvalidator = availabilityInvalidator;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(ConfirmManualBookingCommand command, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var actor = command.ActorUserId.ToString("D");

        var booking = await _dbContext.Bookings
            .Include(b => b.History)
            .FirstOrDefaultAsync(b => b.Id == command.BookingId && !b.IsDeleted, cancellationToken);

        if (booking is null || booking.VenueId != command.VenueId)
        {
            return Result.Failure(BookingErrors.NotFound);
        }

        var result = booking.ConfirmManualDeposit(actor, nowUtc, command.CollectedDeposit, command.Notes);
        if (result.IsFailure)
        {
            return result;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(Error.Conflict("Booking.ConcurrentUpdate", "The booking was changed by another request. Please reload and try again."));
        }

        await AfterCommitAsync(booking, nowUtc, cancellationToken);
        return Result.Success();
    }

    private async Task AfterCommitAsync(Domain.Booking booking, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await RunGuardedAsync(booking, "clear cached availability", () => _availabilityInvalidator.InvalidatePitchAsync(booking.PitchId, cancellationToken));
        await RunGuardedAsync(booking, "broadcast slot status", () => _broadcaster.BroadcastSlotStatusChangedAsync(
            booking.VenueId, booking.PitchId, booking.StartUtc, booking.EndUtc, "booked", cancellationToken));
        await RunGuardedAsync(booking, "broadcast booking update", () => _broadcaster.BroadcastBookingUpdatedAsync(
            booking.PlayerId, booking.VenueId, booking.Id, booking.BookingReference, BookingStatus.Confirmed.ToString(), nowUtc, cancellationToken));
    }

    private async Task RunGuardedAsync(Domain.Booking booking, string step, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Confirm manual booking {BookingId} was saved but the step '{Step}' failed.", booking.Id, step);
        }
    }
}
