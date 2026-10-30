using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Infrastructure;

public sealed class UpcomingBookingsReader : IUpcomingBookingsReader
{
    private static readonly BookingStatus[] _slotHoldingStatuses =
        [BookingStatus.PendingDeposit, BookingStatus.ProofSubmitted, BookingStatus.UnderReview, BookingStatus.Confirmed];

    private readonly BookingDbContext _dbContext;

    public UpcomingBookingsReader(BookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<UpcomingBookingRef>> GetActiveAsync(
        IReadOnlyCollection<Guid> pitchIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var bookings = await _dbContext.Bookings.AsNoTracking()
            .Where(b => pitchIds.Contains(b.PitchId) && b.EndUtc > fromUtc && b.StartUtc < toUtc && _slotHoldingStatuses.Contains(b.Status))
            .OrderBy(b => b.StartUtc)
            .Select(b => new { b.Id, b.BookingReference, b.PitchId, b.StartUtc, b.EndUtc, b.Status, b.HoldExpiresAtUtc })
            .ToListAsync(cancellationToken);

        return bookings.Select(b => new UpcomingBookingRef(b.Id, b.BookingReference, b.PitchId, b.StartUtc, b.EndUtc, b.Status.ToString(),
            b.Status == BookingStatus.PendingDeposit ? b.HoldExpiresAtUtc : null)).ToList();
    }
}
