using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Infrastructure;

/// <summary>
/// The player's bookings in the export. Bookings are accounting records, so deletion keeps them: they reference the
/// player only by an opaque id, which no longer resolves to personal data once the account is anonymized.
/// </summary>
public sealed class BookingPersonalData : IPersonalDataContributor
{
    private readonly BookingDbContext _dbContext;

    public BookingPersonalData(BookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Section => "bookings";

    public async Task<object?> ExportAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var bookings = await _dbContext.Bookings.AsNoTracking()
            .Where(b => b.PlayerId == userId)
            .OrderByDescending(b => b.StartUtc)
            .ToListAsync(cancellationToken);
        return bookings.Select(b => new
        {
            b.BookingReference,
            b.PitchId,
            b.StartUtc,
            b.EndUtc,
            Status = b.Status.ToString(),
            TotalPrice = b.TotalPrice.Amount,
            DepositAmount = b.DepositAmount.Amount
        }).ToList();
    }

    public Task EraseAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
