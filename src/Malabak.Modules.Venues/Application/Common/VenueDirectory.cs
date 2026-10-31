using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Common;

public sealed class VenueDirectory : IVenueDirectory
{
    private readonly VenuesDbContext _dbContext;

    public VenueDirectory(VenuesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // The tenant filter narrows a venue user's own queries to their venues. These answers serve other modules and must not
    // depend on who is asking, so the filter is ignored and deletion is checked explicitly.
    public async Task<Guid?> GetVenueIdOfPitchAsync(Guid pitchId, CancellationToken cancellationToken = default) =>
        await _dbContext.Pitches.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == pitchId && !p.IsDeleted)
            .Select(p => (Guid?)p.VenueId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetVenueNamesAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default)
    {
        if (venueIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await _dbContext.Venues.IgnoreQueryFilters().AsNoTracking()
            .Where(v => venueIds.Contains(v.Id) && !v.IsDeleted)
            .ToDictionaryAsync(v => v.Id, v => v.Name, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetStaffUserIdsAsync(Guid venueId, string permission, CancellationToken cancellationToken = default)
    {
        var ownerId = await _dbContext.Venues.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.Id == venueId && !v.IsDeleted)
            .Select(v => (Guid?)v.OwnerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (ownerId is null)
        {
            return [];
        }

        var staff = await _dbContext.StaffMembers.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.VenueId == venueId && !m.IsDeleted)
            .Select(m => new { m.UserId, m.Role })
            .ToListAsync(cancellationToken);

        return staff
            .Where(m => VenueStaffRolePermissions.For(m.Role).Contains(permission))
            .Select(m => m.UserId)
            .Append(ownerId.Value)
            .Distinct()
            .ToList();
    }
}
