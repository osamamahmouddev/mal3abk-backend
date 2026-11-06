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

    public async Task<VenueSummary?> GetVenueSummaryAsync(Guid venueId, CancellationToken cancellationToken = default) =>
        await _dbContext.Venues.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.Id == venueId && !v.IsDeleted)
            .Select(v => new VenueSummary(v.Id, v.Name, v.Address, v.Latitude, v.Longitude))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<VenueBlockedPeriod>> GetBlockedPeriodsAsync(
        Guid venueId, DateTime fromUtc, DateTime toUtc, Guid? pitchId, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.BlockedSlots.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.VenueId == venueId && !s.IsDeleted && s.StartUtc < toUtc && s.EndUtc > fromUtc);
        if (pitchId is { } pitch && pitch != Guid.Empty)
        {
            query = query.Where(s => s.PitchId == pitch);
        }

        return await query
            .OrderBy(s => s.StartUtc)
            .Select(s => new VenueBlockedPeriod(s.Id, s.PitchId, s.StartUtc, s.EndUtc, s.Reason))
            .ToListAsync(cancellationToken);
    }

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

    public async Task<IReadOnlyDictionary<Guid, string>> GetPitchNamesAsync(IReadOnlyCollection<Guid> pitchIds, CancellationToken cancellationToken = default)
    {
        if (pitchIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await _dbContext.Pitches.IgnoreQueryFilters().AsNoTracking()
            .Where(p => pitchIds.Contains(p.Id) && !p.IsDeleted)
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
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
