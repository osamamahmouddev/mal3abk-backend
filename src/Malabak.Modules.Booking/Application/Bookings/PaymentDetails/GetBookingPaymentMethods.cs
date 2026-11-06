using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Bookings.PaymentDetails;

public sealed record BookingPaymentMethodDto(Guid Id, string MethodType, string RecipientName, string AccountDetails, string? Instructions);

public sealed record GetBookingPaymentMethodsQuery(Guid BookingId, Guid PlayerId) : IQuery<Result<IReadOnlyList<BookingPaymentMethodDto>>>;

/// <summary>
/// Where to send a booking's deposit. Only the booking's player sees it, and only while the deposit can still be paid;
/// anyone else gets "not found" so booking ids cannot be probed for venue account numbers.
/// </summary>
public sealed class GetBookingPaymentMethodsHandler : IQueryHandler<GetBookingPaymentMethodsQuery, Result<IReadOnlyList<BookingPaymentMethodDto>>>
{
    private readonly IBookingDbContext _dbContext;
    private readonly IVenuePaymentDirectory _payments;
    private readonly IClock _clock;

    public GetBookingPaymentMethodsHandler(IBookingDbContext dbContext, IVenuePaymentDirectory payments, IClock clock)
    {
        _dbContext = dbContext;
        _payments = payments;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<BookingPaymentMethodDto>>> HandleAsync(
        GetBookingPaymentMethodsQuery query, CancellationToken cancellationToken = default)
    {
        var booking = await _dbContext.Bookings.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == query.BookingId && !b.IsDeleted, cancellationToken);
        if (booking is null || booking.PlayerId != query.PlayerId)
        {
            return Result.Failure<IReadOnlyList<BookingPaymentMethodDto>>(BookingErrors.NotFound);
        }

        var awaitingDeposit = booking.Status is BookingStatus.PendingDeposit or BookingStatus.Rejected;
        if (!awaitingDeposit || booking.HoldExpiresAtUtc is not { } holdEnd || holdEnd <= _clock.UtcNow)
        {
            return Result.Failure<IReadOnlyList<BookingPaymentMethodDto>>(BookingErrors.HoldExpired);
        }

        var methods = await _payments.RevealForBookingAsync(booking.VenueId, booking.Id, query.PlayerId, cancellationToken);
        return Result.Success<IReadOnlyList<BookingPaymentMethodDto>>(methods
            .Select(m => new BookingPaymentMethodDto(m.Id, m.MethodType, m.RecipientName, m.AccountDetails, m.Instructions))
            .ToList());
    }
}
