using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Infrastructure.Services;

public sealed class BookingPolicyProvider : IBookingPolicyProvider
{
    private readonly VenuesDbContext _dbContext;

    public BookingPolicyProvider(VenuesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BookingPolicySnapshot?> GetCurrentSnapshotAsync(Guid venueId, CancellationToken cancellationToken = default)
    {
        // Booking creation is player-driven, so this read must not be narrowed by a venue-manager tenant scope.
        var current = await _dbContext.Policies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.VenueId == venueId && p.IsCurrent && !p.IsDeleted, cancellationToken);

        return current?.ToSnapshot();
    }
}
