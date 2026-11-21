using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Infrastructure;

/// <summary>Active bookings (held, waiting for review or confirmed) that have not started yet.</summary>
public sealed class UpcomingBookingsCounter : IActiveBookingsCounter
{
    private static readonly BookingStatus[] _active =
        [BookingStatus.PendingDeposit, BookingStatus.ProofSubmitted, BookingStatus.UnderReview, BookingStatus.Confirmed];

    private readonly BookingDbContext _dbContext;
    private readonly IClock _clock;

    public UpcomingBookingsCounter(BookingDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public Task<int> CountForVenueAsync(Guid venueId, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        return _dbContext.Bookings.AsNoTracking()
            .CountAsync(b => b.VenueId == venueId && b.StartUtc > now && _active.Contains(b.Status), cancellationToken);
    }

    public Task<int> CountForPlayerAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        // A manual booking's "player" is the staff member who entered it, not a customer of theirs.
        return _dbContext.Bookings.AsNoTracking()
            .CountAsync(b => b.PlayerId == playerId && !b.IsManualBooking && b.StartUtc > now && _active.Contains(b.Status), cancellationToken);
    }
}
