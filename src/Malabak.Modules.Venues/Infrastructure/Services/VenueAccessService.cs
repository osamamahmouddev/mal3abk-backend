using Malabak.Modules.Venues.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Infrastructure.Services;

/// <summary>
/// Ownership is the only venue relationship today. Staff memberships (TASK-212) plug in here by
/// returning <see cref="VenueAccessLevel.Staff"/> with that member's permission set.
/// </summary>
public sealed class VenueAccessService : IVenueAccessService
{
    private readonly VenuesDbContext _dbContext;

    public VenueAccessService(VenuesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<VenueAccess> GetAccessAsync(Guid userId, Guid venueId, CancellationToken cancellationToken = default)
    {
        // Bypasses the tenant filter: this is the query that decides what the filter should allow.
        var isOwner = await _dbContext.Venues
            .IgnoreQueryFilters()
            .AnyAsync(v => v.Id == venueId && v.OwnerId == userId && !v.IsDeleted, cancellationToken);

        return isOwner ? VenueAccess.Owner(venueId) : VenueAccess.None(venueId);
    }

    public async Task<IReadOnlyList<VenueAccess>> GetAccessibleVenuesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var ownedVenueIds = await _dbContext.Venues
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.OwnerId == userId && !v.IsDeleted)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);

        return ownedVenueIds.Select(VenueAccess.Owner).ToList();
    }
}
