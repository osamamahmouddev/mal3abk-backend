using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Infrastructure.Services;

/// <summary>Owners get every venue-scoped permission; staff get the permission set of their per-venue role.</summary>
public sealed class VenueAccessService : IVenueAccessService
{
    private readonly VenuesDbContext _dbContext;

    public VenueAccessService(VenuesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<VenueAccess> GetAccessAsync(Guid userId, Guid venueId, CancellationToken cancellationToken = default)
    {
        // Bypasses the tenant filter: these are the queries that decide what the filter should allow.
        var isOwner = await _dbContext.Venues
            .IgnoreQueryFilters()
            .AnyAsync(v => v.Id == venueId && v.OwnerId == userId && !v.IsDeleted, cancellationToken);
        if (isOwner)
        {
            return VenueAccess.Owner(venueId);
        }

        var staffRole = await _dbContext.StaffMembers
            .IgnoreQueryFilters()
            .Where(m => m.VenueId == venueId && m.UserId == userId && !m.IsDeleted)
            .Where(m => _dbContext.Venues.IgnoreQueryFilters().Any(v => v.Id == m.VenueId && !v.IsDeleted))
            .Select(m => (VenueStaffRole?)m.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return staffRole is { } role ? VenueAccess.Staff(venueId, role) : VenueAccess.None(venueId);
    }

    public async Task<IReadOnlyList<VenueAccess>> GetAccessibleVenuesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var ownedVenueIds = await _dbContext.Venues
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.OwnerId == userId && !v.IsDeleted)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);

        var staffMemberships = await _dbContext.StaffMembers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.UserId == userId && !m.IsDeleted && !ownedVenueIds.Contains(m.VenueId))
            .Where(m => _dbContext.Venues.IgnoreQueryFilters().Any(v => v.Id == m.VenueId && !v.IsDeleted))
            .Select(m => new { m.VenueId, m.Role })
            .ToListAsync(cancellationToken);

        return ownedVenueIds.Select(VenueAccess.Owner)
            .Concat(staffMemberships.Select(m => VenueAccess.Staff(m.VenueId, m.Role)))
            .ToList();
    }
}
