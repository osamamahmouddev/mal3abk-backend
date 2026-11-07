using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Admin;

/// <summary>A player's booking history for the admin user page (TASK-701). Manual bookings made by staff are not counted.</summary>
public sealed class BookingUserActivityProvider : IUserActivityProvider
{
    private readonly IBookingDbContext _dbContext;

    public BookingUserActivityProvider(IBookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Source => "bookings";

    public async Task<UserActivitySummary> GetAsync(Guid userId, int take, CancellationToken cancellationToken = default)
    {
        var mine = _dbContext.Bookings.AsNoTracking().Where(b => b.PlayerId == userId && !b.IsManualBooking);
        var counts = await mine.GroupBy(b => b.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var recent = await mine.OrderByDescending(b => b.StartUtc).Take(take)
            .Select(b => new { b.BookingReference, b.Status, b.StartUtc, b.Id })
            .ToListAsync(cancellationToken);

        return new UserActivitySummary(
            Source,
            counts.Sum(c => c.Count),
            counts.ToDictionary(c => c.Status.ToString(), c => c.Count),
            recent.Select(b => new UserActivityItem("booking", b.BookingReference, b.Status.ToString(), b.StartUtc, b.Id.ToString("D"))).ToList());
    }
}
