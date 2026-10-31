using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Bookings.Refunds;

public sealed record RecordManualRefundCommand(
    Guid VenueId,
    Guid BookingId,
    decimal Amount,
    string Notes,
    Guid ActorUserId
) : ICommand<Result>;

/// <summary>
/// The venue records a refund it paid by hand (TASK-406). Refunds are tracked apart from the booking's status. One is owed only
/// for a cancelled booking whose deposit was submitted, and it is created when the booking is cancelled; here it is recorded
/// exactly once, for a positive amount no larger than the deposit, and never changed afterwards.
/// </summary>
public sealed class RecordManualRefundHandler : ICommandHandler<RecordManualRefundCommand, Result>
{
    public const int MaxNotesLength = 1000;

    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;

    public RecordManualRefundHandler(IBookingDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result> HandleAsync(RecordManualRefundCommand command, CancellationToken cancellationToken = default)
    {
        var booking = await _dbContext.Bookings.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == command.BookingId && !b.IsDeleted, cancellationToken);

        // Another venue's booking looks like one that does not exist, so ids cannot be probed.
        if (booking is null || booking.VenueId != command.VenueId)
        {
            return Result.Failure(BookingErrors.NotFound);
        }

        if (command.Notes is { Length: > MaxNotesLength })
        {
            return Result.Failure(Error.Validation("Refund.NotesTooLong", $"The notes cannot exceed {MaxNotesLength} characters."));
        }

        var refund = await _dbContext.BookingRefunds.FirstOrDefaultAsync(r => r.BookingId == command.BookingId, cancellationToken);
        if (refund is null || booking.Status != BookingStatus.Cancelled)
        {
            return Result.Failure(BookingErrors.NoRefundDue);
        }

        var recorded = refund.RecordManualRefund(command.Amount, booking.DepositAmount.Amount, command.Notes, command.ActorUserId.ToString("D"), _clock.UtcNow);
        if (recorded.IsFailure)
        {
            return recorded;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A second request recorded or denied it first.
            return Result.Failure(BookingErrors.RefundAlreadyDecided);
        }

        return Result.Success();
    }
}
