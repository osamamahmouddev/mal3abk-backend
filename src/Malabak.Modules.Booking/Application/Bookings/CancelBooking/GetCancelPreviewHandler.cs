using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Bookings.CancelBooking;

public sealed record GetCancelPreviewQuery(Guid BookingId, Guid PlayerId) : IQuery<Result<CancelPreviewResponse>>;

public sealed class GetCancelPreviewHandler : IQueryHandler<GetCancelPreviewQuery, Result<CancelPreviewResponse>>
{
    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;

    public GetCancelPreviewHandler(IBookingDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<CancelPreviewResponse>> HandleAsync(GetCancelPreviewQuery query, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var booking = await _dbContext.Bookings.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == query.BookingId && !b.IsDeleted, cancellationToken);

        if (booking is null)
        {
            return Result.Failure<CancelPreviewResponse>(BookingErrors.NotFound);
        }

        // Someone else's booking looks like one that does not exist, so ids cannot be probed.
        if (booking.PlayerId != query.PlayerId)
        {
            return Result.Failure<CancelPreviewResponse>(BookingErrors.NotFound);
        }

        if (booking.Status is not (BookingStatus.PendingDeposit or BookingStatus.ProofSubmitted or BookingStatus.UnderReview or BookingStatus.Confirmed))
        {
            return Result.Failure<CancelPreviewResponse>(Error.Conflict("Booking.CannotCancel", $"Bookings in status {booking.Status} cannot be cancelled."));
        }

        var leadHours = booking.PolicySnapshot?.CancellationDeadlineHours ?? 24;
        var deadlineUtc = booking.StartUtc.AddHours(-leadHours);
        var canCancel = booking.Status != BookingStatus.Confirmed || nowUtc <= deadlineUtc;

        var refundPercentage = canCancel ? (booking.PolicySnapshot?.RefundPercentage ?? 100) : 0;
        var estimatedRefundAmount = canCancel ? booking.DepositAmount.Amount * (refundPercentage / 100m) : 0m;

        var response = new CancelPreviewResponse(
            CanCancel: canCancel,
            CancellationDeadlineUtc: deadlineUtc,
            RefundPercentage: refundPercentage,
            EstimatedRefundAmount: estimatedRefundAmount,
            Currency: booking.DepositAmount.Currency,
            PolicySummary: booking.PolicySnapshot?.Terms ?? "Standard venue cancellation policy");

        return Result.Success(response);
    }
}
