using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Infrastructure.Repositories;

public sealed class VenueRepository : IVenueRepository
{
    private readonly VenuesDbContext _dbContext;

    public VenueRepository(VenuesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Venue?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Venues
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted, cancellationToken);
    }

    public async Task<Venue?> GetByIdWithPitchesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Venues
            .Include(v => v.Pitches.Where(p => !p.IsDeleted))
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted, cancellationToken);
    }

    public async Task<Venue?> GetByIdWithSchedulesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Venues
            .Include(v => v.OperatingSchedules.Where(s => !s.IsDeleted))
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<Venue>> GetByOwnerIdAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Venues
            .Where(v => v.OwnerId == ownerId && !v.IsDeleted)
            .Include(v => v.Pitches.Where(p => !p.IsDeleted))
            .OrderBy(v => v.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsBySlugAsync(string slug, Guid? excludeVenueId = null, CancellationToken cancellationToken = default)
    {
        var normalized = slug.Trim().ToLowerInvariant();

        // Slugs are unique across all owners, so this check must see venues outside the caller's tenant scope.
        // Slugs must also be unique across all venues including soft-deleted ones, because IX_venues_Slug is unique unconditionally.
        return await _dbContext.Venues
            .IgnoreQueryFilters()
            .AnyAsync(v => v.Slug == normalized && (!excludeVenueId.HasValue || v.Id != excludeVenueId.Value), cancellationToken);
    }

    public void Add(Venue venue)
    {
        _dbContext.Venues.Add(venue);
    }

    public void Delete(Venue venue, DateTime nowUtc)
    {
        venue.MarkDeleted(nowUtc);
    }
}
