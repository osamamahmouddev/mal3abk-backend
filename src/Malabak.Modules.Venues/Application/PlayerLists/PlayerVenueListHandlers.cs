using Malabak.Modules.Venues.Application.SpecialSchedules;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.PlayerLists;

public sealed record VenueCardDto(
    Guid VenueId, string Name, string Slug, string City, string Area, string? CoverPhotoUrl, bool IsFavorite, DateTime? NextAvailableStartUtc, DateTime? ViewedAtUtc);

/// <summary>Per-venue flags for explore/search results (TASK-014 joins these onto its cards).</summary>
public sealed record VenueEngagementFlags(bool IsFavorite, bool RecentlyViewed);

public static class PlayerListErrors
{
    public static readonly Error VenueNotFound = new("Venue.NotFound", "Venue not found.");
    public static readonly Error FavoritesFull = new("Favorites.Full", "You can keep up to 200 favorite venues.");
}

/// <summary>
/// Favorites and recently viewed venues. Lists are private to the player, only reference searchable venues and
/// carry nothing about the player beyond the pairing; they are removed with the account (TASK-222).
/// </summary>
public sealed class PlayerVenueListHandlers
{
    /// <summary>How many business days ahead the "next available" hint looks.</summary>
    public const int HintDays = 2;

    private readonly VenuesDbContext _dbContext;
    private readonly IUpcomingBookingsReader _bookings;
    private readonly IClock _clock;

    public PlayerVenueListHandlers(VenuesDbContext dbContext, IUpcomingBookingsReader bookings, IClock clock)
    {
        _dbContext = dbContext;
        _bookings = bookings;
        _clock = clock;
    }

    /// <summary>Idempotent: adding an existing favorite succeeds without a duplicate.</summary>
    public async Task<Result> AddFavoriteAsync(Guid userId, Guid venueId, CancellationToken cancellationToken = default)
    {
        if (!await IsSearchableAsync(venueId, cancellationToken))
        {
            return Result.Failure(PlayerListErrors.VenueNotFound);
        }

        if (await _dbContext.FavoriteVenues.AnyAsync(f => f.UserId == userId && f.VenueId == venueId, cancellationToken))
        {
            return Result.Success();
        }

        if (await _dbContext.FavoriteVenues.CountAsync(f => f.UserId == userId, cancellationToken) >= FavoriteVenue.MaxPerUser)
        {
            return Result.Failure(PlayerListErrors.FavoritesFull);
        }

        _dbContext.FavoriteVenues.Add(FavoriteVenue.Create(userId, venueId, _clock.UtcNow));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task RemoveFavoriteAsync(Guid userId, Guid venueId, CancellationToken cancellationToken = default)
    {
        var favorite = await _dbContext.FavoriteVenues.FirstOrDefaultAsync(f => f.UserId == userId && f.VenueId == venueId, cancellationToken);
        if (favorite is not null)
        {
            _dbContext.FavoriteVenues.Remove(favorite);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<VenueCardDto>> GetFavoritesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var favorites = await _dbContext.FavoriteVenues.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => f.VenueId)
            .ToListAsync(cancellationToken);

        return await CardsAsync(userId, favorites, new Dictionary<Guid, DateTime>(), withHints: true, cancellationToken);
    }

    /// <summary>Records a venue-detail view, moving it to the top and keeping the list bounded.</summary>
    public async Task<Result> RecordViewAsync(Guid userId, Guid venueId, CancellationToken cancellationToken = default)
    {
        if (!await IsSearchableAsync(venueId, cancellationToken))
        {
            return Result.Failure(PlayerListErrors.VenueNotFound);
        }

        var nowUtc = _clock.UtcNow;
        var history = await _dbContext.RecentlyViewedVenues.Where(r => r.UserId == userId).ToListAsync(cancellationToken);
        var existing = history.Find(r => r.VenueId == venueId);
        if (existing is null)
        {
            existing = RecentlyViewedVenue.Create(userId, venueId, nowUtc);
            _dbContext.RecentlyViewedVenues.Add(existing);
            history.Add(existing);
        }
        else
        {
            existing.Touch(nowUtc);
        }

        _dbContext.RecentlyViewedVenues.RemoveRange(history.OrderByDescending(r => r.ViewedAtUtc).Skip(RecentlyViewedVenue.MaxPerUser));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<IReadOnlyList<VenueCardDto>> GetRecentlyViewedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var history = await _dbContext.RecentlyViewedVenues.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.ViewedAtUtc)
            .Take(RecentlyViewedVenue.MaxPerUser)
            .ToListAsync(cancellationToken);

        return await CardsAsync(
            userId, history.Select(h => h.VenueId).ToList(), history.ToDictionary(h => h.VenueId, h => h.ViewedAtUtc), withHints: false, cancellationToken);
    }

    public async Task ClearRecentlyViewedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var history = await _dbContext.RecentlyViewedVenues.Where(r => r.UserId == userId).ToListAsync(cancellationToken);
        _dbContext.RecentlyViewedVenues.RemoveRange(history);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Flags for a page of explore results; venues without an entry are neither favorite nor viewed.</summary>
    public async Task<IReadOnlyDictionary<Guid, VenueEngagementFlags>> GetFlagsAsync(
        Guid userId, IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default)
    {
        var favorites = await _dbContext.FavoriteVenues.AsNoTracking()
            .Where(f => f.UserId == userId && venueIds.Contains(f.VenueId)).Select(f => f.VenueId).ToListAsync(cancellationToken);
        var viewed = await _dbContext.RecentlyViewedVenues.AsNoTracking()
            .Where(r => r.UserId == userId && venueIds.Contains(r.VenueId)).Select(r => r.VenueId).ToListAsync(cancellationToken);

        return venueIds.Distinct().ToDictionary(id => id, id => new VenueEngagementFlags(favorites.Contains(id), viewed.Contains(id)));
    }

    private Task<bool> IsSearchableAsync(Guid venueId, CancellationToken cancellationToken) =>
        _dbContext.Venues.AnyAsync(
            v => v.Id == venueId && v.ApprovalStatus == VenueApprovalStatus.Approved && v.IsPublished && !v.IsDeleted, cancellationToken);

    private async Task<IReadOnlyList<VenueCardDto>> CardsAsync(
        Guid userId, List<Guid> orderedVenueIds, Dictionary<Guid, DateTime> viewedAt, bool withHints, CancellationToken cancellationToken)
    {
        if (orderedVenueIds.Count == 0)
        {
            return [];
        }

        // Venues that were unpublished or removed since are hidden rather than leaked.
        var venues = await _dbContext.Venues.AsNoTracking()
            .Where(v => orderedVenueIds.Contains(v.Id) && v.ApprovalStatus == VenueApprovalStatus.Approved && v.IsPublished && !v.IsDeleted)
            .ToListAsync(cancellationToken);
        var flags = await GetFlagsAsync(userId, orderedVenueIds, cancellationToken);

        var cards = new List<VenueCardDto>();
        foreach (var venue in orderedVenueIds.Select(id => venues.Find(v => v.Id == id)).OfType<Venue>())
        {
            var hint = withHints ? await NextAvailableStartAsync(venue.Id, cancellationToken) : null;
            cards.Add(new VenueCardDto(
                venue.Id, venue.Name, venue.Slug, venue.City, venue.Area, venue.Photos.FirstOrDefault(), flags[venue.Id].IsFavorite, hint,
                viewedAt.TryGetValue(venue.Id, out var at) ? at : null));
        }

        return cards;
    }

    /// <summary>
    /// Availability hint: the earliest bookable start over the next <see cref="HintDays"/> business days on any active
    /// pitch, skipping blocked slots and slots held by bookings. A hint only; booking re-checks everything.
    /// </summary>
    private async Task<DateTime?> NextAvailableStartAsync(Guid venueId, CancellationToken cancellationToken)
    {
        var activePitches = await _dbContext.Pitches.AsNoTracking()
            .Where(p => p.VenueId == venueId && p.IsActive && !p.IsDeleted).Select(p => p.Id).ToListAsync(cancellationToken);
        var states = (await ScheduleStateLoader.LoadAsync(_dbContext, venueId, null, cancellationToken))
            .Where(s => activePitches.Contains(s.Key)).ToList();
        if (states.Count == 0)
        {
            return null;
        }

        var nowUtc = _clock.UtcNow;
        var untilUtc = nowUtc.AddDays(HintDays + 1);
        var taken = await _bookings.GetActiveAsync(activePitches, nowUtc, untilUtc, cancellationToken);
        var blocked = await _dbContext.BlockedSlots.AsNoTracking()
            .Where(b => b.VenueId == venueId && !b.IsDeleted && b.EndUtc > nowUtc && b.StartUtc < untilUtc)
            .Select(b => new { b.PitchId, b.StartUtc, b.EndUtc })
            .ToListAsync(cancellationToken);

        DateTime? best = null;
        foreach (var (pitchId, state) in states)
        {
            for (var day = 0; day < HintDays; day++)
            {
                var windows = AvailabilityCalculator.OpenWindows(state, _clock.CairoToday.AddDays(day), _clock.CairoTimeZone);
                var minutes = state.Rules.MinDurationMinutes;
                var free = AvailabilityCalculator.StartTimes(windows, state.Rules, nowUtc).FirstOrDefault(start =>
                    !taken.Any(b => b.PitchId == pitchId && b.StartUtc < start.AddMinutes(minutes) && b.EndUtc > start)
                    && !blocked.Exists(b => b.PitchId == pitchId && b.StartUtc < start.AddMinutes(minutes) && b.EndUtc > start));
                if (free != default)
                {
                    best = best is null || free < best ? free : best;
                    break;
                }
            }
        }

        return best;
    }
}
