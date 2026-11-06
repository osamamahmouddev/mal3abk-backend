using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Admin;

public sealed class VenueBookingStatsReader : IVenueBookingStatsReader
{
    private readonly IBookingDbContext _dbContext;

    public VenueBookingStatsReader(IBookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<VenueBookingStats> GetAsync(Guid venueId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.Bookings.AsNoTracking()
            .Where(b => b.VenueId == venueId && !b.IsDeleted && b.StartUtc >= fromUtc && b.StartUtc < toUtc && b.Status != BookingStatus.Draft)
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Value = g.Sum(b => b.TotalPrice.Amount) })
            .ToListAsync(cancellationToken);
        var last = await _dbContext.Bookings.AsNoTracking()
            .Where(b => b.VenueId == venueId && !b.IsDeleted && b.Status != BookingStatus.Draft)
            .OrderByDescending(b => b.StartUtc).Select(b => (DateTime?)b.StartUtc).FirstOrDefaultAsync(cancellationToken);

        int Count(params BookingStatus[] statuses) => rows.Where(r => statuses.Contains(r.Status)).Sum(r => r.Count);
        return new VenueBookingStats(
            rows.Sum(r => r.Count),
            Count(BookingStatus.Confirmed),
            Count(BookingStatus.Completed),
            Count(BookingStatus.Cancelled),
            Count(BookingStatus.NoShow),
            Count(BookingStatus.PendingDeposit, BookingStatus.ProofSubmitted, BookingStatus.UnderReview),
            rows.Where(r => r.Status is BookingStatus.Confirmed or BookingStatus.Completed).Sum(r => r.Value),
            "EGP",
            last);
    }
}
