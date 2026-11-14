using Malabak.Modules.Venues.Application.Staff;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Subscriptions;

/// <summary>Current usage of a venue owner, for plan limits (TASK-060) and the billing page (TASK-507).</summary>
public sealed class OwnerUsageReader : IOwnerUsageReader
{
    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;

    public OwnerUsageReader(VenuesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<OwnerUsage> GetAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        var venueIds = await _dbContext.Venues.AsNoTracking().Where(v => v.OwnerId == ownerId && !v.IsDeleted).Select(v => v.Id).ToListAsync(cancellationToken);
        var pitches = venueIds.Count == 0
            ? 0
            : await _dbContext.Pitches.AsNoTracking().CountAsync(p => venueIds.Contains(p.VenueId) && !p.IsDeleted, cancellationToken);
        var maxStaff = 0;
        foreach (var venueId in venueIds)
        {
            maxStaff = Math.Max(maxStaff, await StaffCountAsync(venueId, cancellationToken));
        }

        return new OwnerUsage(venueIds.Count, pitches, maxStaff);
    }

    /// <summary>Active staff plus invitations still pending (an invitation reserves a seat).</summary>
    public async Task<int> StaffCountAsync(Guid venueId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var members = await _dbContext.StaffMembers.AsNoTracking().CountAsync(m => m.VenueId == venueId && !m.IsDeleted, cancellationToken);
        var pending = await _dbContext.StaffInvitations.AsNoTracking()
            .CountAsync(i => i.VenueId == venueId && i.Status == Domain.StaffInvitationStatus.Pending && i.ExpiresAtUtc > now, cancellationToken);
        return members + pending;
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<OwnerVenueInfo>>> GetOwnerVenuesBatchAsync(IReadOnlyCollection<Guid> ownerIds, CancellationToken cancellationToken = default)
    {
        if (ownerIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<OwnerVenueInfo>>();
        }

        var venues = await _dbContext.Venues.AsNoTracking()
            .Where(v => ownerIds.Contains(v.OwnerId) && !v.IsDeleted)
            .OrderBy(v => v.Name)
            .Select(v => new { v.OwnerId, v.Id, v.Name, v.City, v.IsPublished })
            .ToListAsync(cancellationToken);

        return venues
            .GroupBy(v => v.OwnerId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<OwnerVenueInfo>)g.Select(x => new OwnerVenueInfo(x.Id, x.Name, x.City, x.IsPublished)).ToList());
    }
}

/// <summary>
/// Staff limit from the owner's plan (TASK-060), replacing the fixed default. The platform-wide hard cap stays as a safety
/// net even on plans without a staff limit.
/// </summary>
public sealed class EntitlementStaffQuotaPolicy : IStaffQuotaPolicy
{
    private readonly VenuesDbContext _dbContext;
    private readonly IEntitlementService _entitlements;

    public EntitlementStaffQuotaPolicy(VenuesDbContext dbContext, IEntitlementService entitlements)
    {
        _dbContext = dbContext;
        _entitlements = entitlements;
    }

    public async Task<bool> CanAddAsync(Guid venueId, int currentCount, CancellationToken cancellationToken = default)
    {
        if (currentCount >= DefaultStaffQuotaPolicy.MaxStaffPerVenue)
        {
            return false;
        }

        var ownerId = await _dbContext.Venues.AsNoTracking().Where(v => v.Id == venueId).Select(v => (Guid?)v.OwnerId).FirstOrDefaultAsync(cancellationToken);
        return ownerId is { } owner && (await _entitlements.EnsureWithinLimitAsync(owner, EntitlementResource.StaffPerVenue, currentCount, cancellationToken)).IsSuccess;
    }
}
