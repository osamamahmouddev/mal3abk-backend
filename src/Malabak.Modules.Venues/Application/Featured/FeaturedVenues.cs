using Malabak.Modules.Venues.Application.Explore;
using Malabak.Modules.Venues.Application.Home;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Featured;

public sealed record FeaturedPlacementInput(Guid VenueId, string? Slot, string? Title, DateTime StartsAtUtc, DateTime EndsAtUtc, int SortOrder);

public sealed record FeaturedPlacementDto(
    Guid Id, Guid VenueId, string VenueName, string Slot, string? Title, DateTime StartsAtUtc, DateTime EndsAtUtc, int SortOrder, bool IsLive, bool IsCancelled,
    DateTime CreatedAtUtc);

/// <summary>
/// Featured venues and promotions (TASK-711). Admins schedule placements; players only see them while the matching feature
/// flag is on (<c>venues.featured</c> for explore, <c>home.sponsored</c> for the home feed), only for live venues, and always
/// with the sponsored label.
/// </summary>
public sealed class FeaturedVenueService
{
    public const int MaxConcurrentPerSlot = 10;

    private readonly VenuesDbContext _dbContext;
    private readonly VenueExplore _explore;
    private readonly IFeatureFlags _flags;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public FeaturedVenueService(VenuesDbContext dbContext, VenueExplore explore, IFeatureFlags flags, IAuditLog audit, IClock clock)
    {
        _dbContext = dbContext;
        _explore = explore;
        _flags = flags;
        _audit = audit;
        _clock = clock;
    }

    // ---- public ----

    /// <summary>Live sponsored items for a slot, in admin order; empty while the slot's flag is off.</summary>
    public async Task<IReadOnlyList<SponsoredItem>> LiveAsync(FeaturedPlacementSlot slot, Guid? userId, CancellationToken cancellationToken = default)
    {
        var flag = slot == FeaturedPlacementSlot.Home ? FeatureFlagKeys.SponsoredHomeItems : FeatureFlagKeys.FeaturedVenues;
        if (!await _flags.IsEnabledAsync(flag, cancellationToken))
        {
            return [];
        }

        var now = _clock.UtcNow;
        var placements = await _dbContext.FeaturedPlacements.AsNoTracking()
            .Where(p => p.Slot == slot && p.CancelledAtUtc == null && p.StartsAtUtc <= now && p.EndsAtUtc > now)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.StartsAtUtc)
            .Take(MaxConcurrentPerSlot)
            .ToListAsync(cancellationToken);
        if (placements.Count == 0)
        {
            return [];
        }

        // Explore only returns searchable venues, so a venue unpublished or suspended since scheduling drops out by itself.
        var ids = placements.Select(p => p.VenueId).Distinct().ToList();
        var cards = (await _explore.RankAsync(new ExploreQuery(VenueIds: ids), [ExploreSort.Name], ids.Count, userId, cancellationToken))[0]
            .ToDictionary(c => c.VenueId);
        return placements.Where(p => cards.ContainsKey(p.VenueId))
            .Select(p => new SponsoredItem(FeaturedPlacement.SponsoredLabel, p.Title ?? cards[p.VenueId].Name, cards[p.VenueId]))
            .ToList();
    }

    // ---- admin ----

    public async Task<IReadOnlyList<FeaturedPlacementDto>> ListAsync(bool includeEnded, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var query = _dbContext.FeaturedPlacements.AsNoTracking();
        if (!includeEnded)
        {
            query = query.Where(p => p.CancelledAtUtc == null && p.EndsAtUtc > now);
        }

        var rows = await query.OrderBy(p => p.Slot).ThenBy(p => p.StartsAtUtc).Take(200).ToListAsync(cancellationToken);
        var names = await NamesAsync(rows.Select(r => r.VenueId), cancellationToken);
        return rows.Select(p => ToDto(p, names, now)).ToList();
    }

    public async Task<Result<FeaturedPlacementDto>> CreateAsync(FeaturedPlacementInput input, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<FeaturedPlacementSlot>(input.Slot, ignoreCase: true, out var slot))
        {
            return Result.Failure<FeaturedPlacementDto>(FeaturedErrors.Invalid);
        }

        if (!await _dbContext.Venues.AnyAsync(v => v.Id == input.VenueId && !v.IsDeleted && v.ApprovalStatus == VenueApprovalStatus.Approved && v.IsPublished, cancellationToken))
        {
            return Result.Failure<FeaturedPlacementDto>(FeaturedErrors.VenueNotLive);
        }

        var placement = FeaturedPlacement.Schedule(input.VenueId, slot, input.Title, input.StartsAtUtc, input.EndsAtUtc, input.SortOrder, adminId, _clock.UtcNow);
        if (placement.IsFailure)
        {
            return Result.Failure<FeaturedPlacementDto>(placement.Error);
        }

        if (await OverlappingAsync(slot, input.StartsAtUtc, input.EndsAtUtc, null, cancellationToken) >= MaxConcurrentPerSlot)
        {
            return Result.Failure<FeaturedPlacementDto>(FeaturedErrors.SlotFull);
        }

        _dbContext.FeaturedPlacements.Add(placement.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AuditAsync("admin.featured.created", placement.Value, adminId, cancellationToken);
        return Result.Success(await DtoAsync(placement.Value, cancellationToken));
    }

    public async Task<Result<FeaturedPlacementDto>> UpdateAsync(Guid id, FeaturedPlacementInput input, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<FeaturedPlacementSlot>(input.Slot, ignoreCase: true, out var slot))
        {
            return Result.Failure<FeaturedPlacementDto>(FeaturedErrors.Invalid);
        }

        var placement = await _dbContext.FeaturedPlacements.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (placement is null)
        {
            return Result.Failure<FeaturedPlacementDto>(FeaturedErrors.NotFound);
        }

        var changed = placement.Change(slot, input.Title, input.StartsAtUtc, input.EndsAtUtc, input.SortOrder, _clock.UtcNow);
        if (changed.IsFailure)
        {
            return Result.Failure<FeaturedPlacementDto>(changed.Error);
        }

        if (await OverlappingAsync(slot, input.StartsAtUtc, input.EndsAtUtc, id, cancellationToken) >= MaxConcurrentPerSlot)
        {
            return Result.Failure<FeaturedPlacementDto>(FeaturedErrors.SlotFull);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await AuditAsync("admin.featured.updated", placement, adminId, cancellationToken);
        return Result.Success(await DtoAsync(placement, cancellationToken));
    }

    public async Task<Result<FeaturedPlacementDto>> CancelAsync(Guid id, Guid adminId, CancellationToken cancellationToken = default)
    {
        var placement = await _dbContext.FeaturedPlacements.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (placement is null)
        {
            return Result.Failure<FeaturedPlacementDto>(FeaturedErrors.NotFound);
        }

        var cancelled = placement.Cancel(_clock.UtcNow);
        if (cancelled.IsFailure)
        {
            return Result.Failure<FeaturedPlacementDto>(cancelled.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await AuditAsync("admin.featured.cancelled", placement, adminId, cancellationToken);
        return Result.Success(await DtoAsync(placement, cancellationToken));
    }

    private Task<int> OverlappingAsync(FeaturedPlacementSlot slot, DateTime startsAtUtc, DateTime endsAtUtc, Guid? except, CancellationToken cancellationToken) =>
        _dbContext.FeaturedPlacements.CountAsync(
            p => p.Slot == slot && p.CancelledAtUtc == null && p.StartsAtUtc < endsAtUtc && p.EndsAtUtc > startsAtUtc && p.Id != except, cancellationToken);

    private Task AuditAsync(string action, FeaturedPlacement p, Guid adminId, CancellationToken cancellationToken) =>
        _audit.RecordAsync(new AuditEntry(action, nameof(FeaturedPlacement), p.Id.ToString("D"), adminId, AuditActorType.Admin,
            After: new { p.VenueId, Slot = p.Slot.ToString(), p.Title, p.StartsAtUtc, p.EndsAtUtc, p.SortOrder, p.CancelledAtUtc }), cancellationToken);

    private async Task<FeaturedPlacementDto> DtoAsync(FeaturedPlacement p, CancellationToken cancellationToken) =>
        ToDto(p, await NamesAsync([p.VenueId], cancellationToken), _clock.UtcNow);

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> venueIds, CancellationToken cancellationToken)
    {
        var ids = venueIds.Distinct().ToList();
        return await _dbContext.Venues.AsNoTracking().Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Name, cancellationToken);
    }

    private static FeaturedPlacementDto ToDto(FeaturedPlacement p, Dictionary<Guid, string> names, DateTime now) =>
        new(p.Id, p.VenueId, names.GetValueOrDefault(p.VenueId, string.Empty), p.Slot.ToString(), p.Title, p.StartsAtUtc, p.EndsAtUtc, p.SortOrder,
            p.IsLiveAt(now), p.CancelledAtUtc is not null, p.CreatedAtUtc);
}
