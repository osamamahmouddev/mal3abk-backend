using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.Modules.Venues.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Api.Infrastructure;

public sealed class BookingConflictChecker : IBookingConflictChecker
{
    private readonly BookingDbContext _bookingDbContext;

    public BookingConflictChecker(BookingDbContext bookingDbContext)
    {
        _bookingDbContext = bookingDbContext;
    }

    public async Task<bool> HasActiveBookingsAsync(
        Guid pitchId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken = default)
    {
        var activeStatuses = new[]
        {
            BookingStatus.PendingDeposit,
            BookingStatus.ProofSubmitted,
            BookingStatus.UnderReview,
            BookingStatus.Confirmed
        };

        return await _bookingDbContext.Bookings
            .AnyAsync(b => b.PitchId == pitchId
                && activeStatuses.Contains(b.Status)
                && b.StartUtc < endUtc
                && b.EndUtc > startUtc,
                cancellationToken);
    }
}
