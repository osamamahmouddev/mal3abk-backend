using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Trash;

/// <summary>
/// Deleting a venue from the admin console takes it off the app at once (its pitches can no longer be quoted or booked) and
/// keeps it restorable. A venue with bookings still ahead is not deleted, so no player loses a slot without being told.
/// </summary>
public sealed class VenueTrashProvider : ITrashProvider
{
    private readonly VenuesDbContext _dbContext;
    private readonly IActiveBookingsCounter _bookings;
    private readonly IClock _clock;

    public VenueTrashProvider(VenuesDbContext dbContext, IActiveBookingsCounter bookings, IClock clock)
    {
        _dbContext = dbContext;
        _bookings = bookings;
        _clock = clock;
    }

    public string Kind => "venue";

    public async Task<string?> GetNameAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Venues.AsNoTracking().Where(v => v.Id == id && !v.IsDeleted).Select(v => v.Name).FirstOrDefaultAsync(cancellationToken);

    public async Task<Result<TrashedItem>> MoveToTrashAsync(Guid id, Guid adminId, CancellationToken cancellationToken = default)
    {
        // Explicit: the Venues context replaces the soft-delete filter with its tenant filter.
        var venue = await _dbContext.Venues.FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<TrashedItem>(TrashErrors.NotFound);
        }

        var upcoming = await _bookings.CountForVenueAsync(id, cancellationToken);
        if (upcoming > 0)
        {
            return Result.Failure<TrashedItem>(TrashErrors.HasUpcomingBookings(upcoming));
        }

        venue.MoveToTrash(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(new TrashedItem(venue.Name, string.Join(" · ", new[] { venue.Area, venue.City }.Where(s => !string.IsNullOrWhiteSpace(s)))));
    }

    public async Task<Result> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var venue = await _dbContext.Venues.IgnoreQueryFilters().FirstOrDefaultAsync(v => v.Id == id && v.IsDeleted, cancellationToken);
        if (venue is null)
        {
            return Result.Failure(TrashErrors.CannotRestore);
        }

        venue.RestoreFromTrash(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> PurgeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var venue = await _dbContext.Venues.IgnoreQueryFilters().FirstOrDefaultAsync(v => v.Id == id && v.IsDeleted, cancellationToken);
        if (venue is null)
        {
            return Result.Failure(TrashErrors.NotFound);
        }

        venue.Purge(_clock.UtcNow);

        // The venue's own transfer accounts (encrypted account numbers) are financial details of a business that is gone.
        var accounts = await _dbContext.PaymentMethods.IgnoreQueryFilters().Where(m => m.VenueId == id).ToListAsync(cancellationToken);
        _dbContext.HardDelete(accounts);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
