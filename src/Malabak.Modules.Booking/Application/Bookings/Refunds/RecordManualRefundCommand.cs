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

public sealed class RecordManualRefundHandler : ICommandHandler<RecordManualRefundCommand, Result>
{
    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;

    public RecordManualRefundHandler(IBookingDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result> HandleAsync(RecordManualRefundCommand command, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var booking = await _dbContext.Bookings.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == command.BookingId && !b.IsDeleted, cancellationToken);

        if (booking is null)
        {
            return Result.Failure(BookingErrors.NotFound);
        }

        if (booking.VenueId != command.VenueId)
        {
            return Result.Failure(Error.Forbidden("Booking.Forbidden", "Booking does not belong to the specified venue."));
        }

        var refund = await _dbContext.BookingRefunds
            .FirstOrDefaultAsync(r => r.BookingId == command.BookingId, cancellationToken);

        if (refund is null)
        {
            refund = Domain.BookingRefund.CreatePending(
                Guid.NewGuid(),
                booking.Id,
                command.Amount,
                booking.DepositAmount.Currency,
                "Manual refund",
                booking.PolicySnapshot?.Terms,
                nowUtc);

            _dbContext.BookingRefunds.Add(refund);
        }

        refund.RecordManualRefund(command.Amount, command.Notes, command.ActorUserId.ToString("D"), nowUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
