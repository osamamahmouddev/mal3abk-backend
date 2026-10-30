using System.Text.RegularExpressions;
using Malabak.Modules.Venues.Application.Availability;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Reference;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Explore;

public enum ExploreSort
{
    /// <summary>Nearest first; requires coordinates (falls back to Popularity without them).</summary>
    Distance = 0,

    /// <summary>Lowest starting hourly price first.</summary>
    Price = 1,

    /// <summary>Most favorited first (stands in for rating until reviews exist).</summary>
    Popularity = 2,

    Name = 3
}

/// <summary>All filters are optional and whitelisted; unknown codes are rejected rather than ignored.</summary>
public sealed record ExploreQuery(
    double? Latitude = null,
    double? Longitude = null,
    double? RadiusKm = null,
    string? City = null,
    string? Area = null,
    DateOnly? Date = null,
    TimeOnly? From = null,
    TimeOnly? To = null,
    decimal? MaxPrice = null,
    string? PitchType = null,
    string? Surface = null,
    IReadOnlyList<string>? Facilities = null,
    ExploreSort Sort = ExploreSort.Distance,
    int Page = 1,
    int PageSize = 20,
    MapBounds? Bounds = null);

/// <summary>Visible map area in degrees (south-west and north-east corners).</summary>
public sealed record MapBounds(double South, double West, double North, double East);

public sealed record ExploreVenueCard(
    Guid VenueId,
    string Name,
    string Slug,
    string City,
    string Area,
    string? CoverPhotoUrl,
    double? Latitude,
    double? Longitude,
    double? DistanceKm,
    decimal? FromHourlyPrice,
    string Currency,
    IReadOnlyList<string> PitchTypes,
    IReadOnlyList<string> Surfaces,
    IReadOnlyList<string> Facilities,
    int FavoritesCount,
    DateTime? NextAvailableStartUtc,
    bool IsFavorite = false);

/// <summary>A filter the player could relax when nothing matched, and how many venues would then match.</summary>
public sealed record ExploreSuggestion(string Code, string Message, int MatchCount);

public sealed record ExploreResult(
    IReadOnlyList<ExploreVenueCard> Items, int Page, int PageSize, int TotalCount, bool HasMore, IReadOnlyList<ExploreSuggestion> Suggestions);

public sealed record MapVenuePin(
    Guid VenueId, string Name, double Latitude, double Longitude, decimal? FromHourlyPrice, string Currency, DateTime? NextAvailableStartUtc, bool IsFavorite);

public sealed record MapSearchResult(IReadOnlyList<MapVenuePin> Pins, int TotalCount, bool Truncated);

public static class ExploreErrors
{
    public static readonly Error InvalidQuery = new("Explore.InvalidQuery",
        "Check the filters: coordinates must come together and be valid, radius 1-100 km, page size 1-50, time range needs a date, map bounds valid and at most 2 degrees wide.");
    public static readonly Error UnknownCode = new("Explore.UnknownCode", "Unknown pitch type, surface or facility code (see GET /reference).");
}

/// <summary>
/// Venue explore (TASK-022). Only approved and published venues. Coordinates narrow candidates with an indexable
/// bounding box in SQL, then exact Haversine distance in memory; the other filters run on the small candidate set.
/// A date (and optional local time range) keeps venues with at least one available slot, using cached availability.
/// Cards carry the next free slot (today or tomorrow, or on the requested date) and the player's favorite flag; an empty
/// result lists which relaxed filter would bring venues back.
/// </summary>
public sealed partial class VenueExplore
{
    public const int MaxCandidates = 500;
    public const int MaxAvailabilityChecks = 60;
    public const double DefaultRadiusKm = 25;
    public const double MaxBoundsSpanDegrees = 2;
    public const int MaxMapPins = 200;

    private readonly VenuesDbContext _dbContext;
    private readonly CachedVenueAvailability _availability;
    private readonly IClock _clock;

    public VenueExplore(VenuesDbContext dbContext, CachedVenueAvailability availability, IClock clock)
    {
        _dbContext = dbContext;
        _availability = availability;
        _clock = clock;
    }

    /// <summary>Paged venue cards. <paramref name="userId"/> (optional) adds the favorite flag.</summary>
    public async Task<Result<ExploreResult>> SearchAsync(ExploreQuery query, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        var invalid = Validate(query);
        if (invalid is not null)
        {
            return Result.Failure<ExploreResult>(invalid);
        }

        var matches = await FilterAsync(query, cancellationToken);
        var sorted = Sort(matches, query).ToList();
        var page = sorted.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
        page = await WithFavoritesAsync(page, userId, cancellationToken);
        if (query.Date is null)
        {
            page = await WithNextAvailableAsync(page, cancellationToken);
        }

        var suggestions = sorted.Count == 0 ? await SuggestAsync(query, cancellationToken) : [];
        return Result.Success(new ExploreResult(page, query.Page, query.PageSize, sorted.Count, query.Page * query.PageSize < sorted.Count, suggestions));
    }

    /// <summary>
    /// Map view (TASK-302): venues with coordinates inside the visible bounds and the same filters as explore, most popular
    /// first, capped at <see cref="MaxMapPins"/> (zoom in to see the rest).
    /// </summary>
    public async Task<Result<MapSearchResult>> MapAsync(ExploreQuery query, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        var invalid = query.Bounds is null ? ExploreErrors.InvalidQuery : Validate(query);
        if (invalid is not null)
        {
            return Result.Failure<MapSearchResult>(invalid);
        }

        var matches = await FilterAsync(query, cancellationToken);
        var top = Sort(matches, query with { Sort = ExploreSort.Popularity }).Take(MaxMapPins).ToList();
        top = await WithFavoritesAsync(top, userId, cancellationToken);
        var pins = top
            .Select(c => new MapVenuePin(c.VenueId, c.Name, c.Latitude!.Value, c.Longitude!.Value, c.FromHourlyPrice, c.Currency, c.NextAvailableStartUtc, c.IsFavorite))
            .ToList();
        return Result.Success(new MapSearchResult(pins, matches.Count, matches.Count > pins.Count));
    }

    /// <summary>All venues matching the query (unsorted, unpaged); used by explore, map search and suggestions.</summary>
    public async Task<List<ExploreVenueCard>> FilterAsync(ExploreQuery query, CancellationToken cancellationToken)
    {
        var candidates = await LoadCandidatesAsync(query, cancellationToken);
        var radius = query.RadiusKm ?? DefaultRadiusKm;
        var capacity = PitchCapacityFor(query.PitchType);
        var wantedFacilities = (query.Facilities ?? []).Select(FacilityCode).ToHashSet();

        var cards = new List<ExploreVenueCard>();
        foreach (var c in candidates)
        {
            var pitches = c.Pitches
                .Where(p => (capacity is null || p.Capacity == capacity) && (query.Surface is null || string.Equals(p.TurfType, query.Surface, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            double? distance = query.Latitude is { } lat && query.Longitude is { } lng && c.Latitude is { } vLat && c.Longitude is { } vLng
                ? GeoMath.HaversineKm(lat, lng, vLat, vLng)
                : null;
            var fromPrice = FromPrice(c, pitches);

            if (pitches.Count == 0
                || (query.Bounds is null && query.Latitude is not null && (distance is null || distance > radius))
                || !wantedFacilities.IsSubsetOf(c.Facilities.Select(f => f.ToLowerInvariant()))
                || (query.MaxPrice is { } max && (fromPrice is null || fromPrice > max)))
            {
                continue;
            }

            cards.Add(new ExploreVenueCard(
                c.Id, c.Name, c.Slug, c.City, c.Area, c.Photos.FirstOrDefault(), c.Latitude, c.Longitude,
                distance is { } d ? Math.Round(d, 2) : null, fromPrice, c.Currency,
                pitches.Select(p => $"{p.Capacity}v{p.Capacity}").Distinct().Order().ToList(),
                pitches.Select(p => p.TurfType).Distinct().Order().ToList(),
                c.Facilities.ToList(), c.FavoritesCount, null));
        }

        return query.Date is { } date ? await KeepAvailableAsync(cards, date, query, cancellationToken) : cards;
    }

    private async Task<List<VenueCandidate>> LoadCandidatesAsync(ExploreQuery query, CancellationToken cancellationToken)
    {
        var venues = _dbContext.Venues.AsNoTracking()
            .Where(v => v.ApprovalStatus == VenueApprovalStatus.Approved && v.IsPublished && !v.IsDeleted);

        if (query.Bounds is { } bounds)
        {
            venues = venues.Where(v => v.Latitude != null && v.Longitude != null
                && v.Latitude >= bounds.South && v.Latitude <= bounds.North
                && v.Longitude >= bounds.West && v.Longitude <= bounds.East);
        }
        else if (query.Latitude is { } lat && query.Longitude is { } lng)
        {
            var box = GeoMath.BoundingBox(lat, lng, query.RadiusKm ?? DefaultRadiusKm);
            venues = venues.Where(v => v.Latitude != null && v.Longitude != null
                && v.Latitude >= box.MinLatitude && v.Latitude <= box.MaxLatitude
                && v.Longitude >= box.MinLongitude && v.Longitude <= box.MaxLongitude);
        }

        var (cities, areas) = PlaceVariants(query.City, query.Area);
        if (cities is not null)
        {
            venues = venues.Where(v => cities.Contains(v.City.ToLower()));
        }

        if (areas is not null)
        {
            venues = venues.Where(v => areas.Contains(v.Area.ToLower()));
        }

        var list = await venues
            .OrderBy(v => v.Id)
            .Take(MaxCandidates)
            .Select(v => new { v.Id, v.Name, v.Slug, v.City, v.Area, v.Latitude, v.Longitude, v.Facilities, v.Photos })
            .ToListAsync(cancellationToken);
        var ids = list.Select(v => v.Id).ToList();

        var pitches = await _dbContext.Pitches.AsNoTracking()
            .Where(p => ids.Contains(p.VenueId) && p.IsActive && !p.IsDeleted)
            .Select(p => new PitchCandidate(p.Id, p.VenueId, p.Capacity, p.TurfType))
            .ToListAsync(cancellationToken);
        var baseRules = await _dbContext.PriceRules.AsNoTracking()
            .Where(r => ids.Contains(r.VenueId) && r.Kind == PriceRuleKind.Base && r.IsActive && !r.IsDeleted)
            .Select(r => new { r.VenueId, r.PitchId, r.Amount, r.Currency })
            .ToListAsync(cancellationToken);
        var favorites = await _dbContext.FavoriteVenues.AsNoTracking()
            .Where(f => ids.Contains(f.VenueId))
            .GroupBy(f => f.VenueId)
            .Select(g => new { VenueId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return list.Select(v => new VenueCandidate(
            v.Id, v.Name, v.Slug, v.City, v.Area, v.Latitude, v.Longitude, v.Facilities, v.Photos,
            pitches.Where(p => p.VenueId == v.Id).ToList(),
            baseRules.Where(r => r.VenueId == v.Id).Select(r => (r.PitchId, r.Amount ?? 0m)).ToList(),
            baseRules.FirstOrDefault(r => r.VenueId == v.Id)?.Currency ?? Money.DefaultCurrency,
            favorites.FirstOrDefault(f => f.VenueId == v.Id)?.Count ?? 0)).ToList();
    }

    /// <summary>Keeps venues with an available slot on the date (inside the optional local time range) and records the first one.</summary>
    private async Task<List<ExploreVenueCard>> KeepAvailableAsync(List<ExploreVenueCard> cards, DateOnly date, ExploreQuery query, CancellationToken cancellationToken)
    {
        var kept = new List<ExploreVenueCard>();
        foreach (var card in Sort(cards, query).Take(MaxAvailabilityChecks))
        {
            var availability = await _availability.GetAsync(card.VenueId, date, null, cancellationToken);
            if (availability.IsFailure)
            {
                continue;
            }

            var first = availability.Value.Availability.Pitches
                .SelectMany(p => p.Slots)
                .Where(s => s.State == SlotState.Available && InTimeRange(TimeOnly.FromDateTime(s.StartLocal), query.From, query.To))
                .OrderBy(s => s.StartUtc)
                .FirstOrDefault();
            if (first is not null)
            {
                kept.Add(card with { NextAvailableStartUtc = first.StartUtc });
            }
        }

        return kept;
    }

    private async Task<List<ExploreVenueCard>> WithFavoritesAsync(List<ExploreVenueCard> cards, Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is not { } user || cards.Count == 0)
        {
            return cards;
        }

        var ids = cards.Select(c => c.VenueId).ToList();
        var favorites = await _dbContext.FavoriteVenues.AsNoTracking()
            .Where(f => f.UserId == user && ids.Contains(f.VenueId))
            .Select(f => f.VenueId)
            .ToListAsync(cancellationToken);
        return cards.Select(c => c with { IsFavorite = favorites.Contains(c.VenueId) }).ToList();
    }

    /// <summary>First free slot today or tomorrow for each card on the page (served from the availability cache).</summary>
    private async Task<List<ExploreVenueCard>> WithNextAvailableAsync(List<ExploreVenueCard> cards, CancellationToken cancellationToken)
    {
        var result = new List<ExploreVenueCard>(cards.Count);
        foreach (var card in cards)
        {
            DateTime? next = null;
            for (var day = _clock.CairoToday; next is null && day <= _clock.CairoToday.AddDays(1); day = day.AddDays(1))
            {
                var availability = await _availability.GetAsync(card.VenueId, day, null, cancellationToken);
                if (availability.IsSuccess)
                {
                    next = availability.Value.Availability.Pitches.SelectMany(p => p.Slots)
                        .Where(s => s.State == SlotState.Available)
                        .Select(s => (DateTime?)s.StartUtc)
                        .Min();
                }
            }

            result.Add(card with { NextAvailableStartUtc = next });
        }

        return result;
    }

    /// <summary>When nothing matched: which single relaxed filter would bring results back, and how many.</summary>
    private async Task<List<ExploreSuggestion>> SuggestAsync(ExploreQuery query, CancellationToken cancellationToken)
    {
        var options = new List<(string Code, string Message, ExploreQuery Relaxed)>();
        if (query.Latitude is not null && query.Bounds is null && (query.RadiusKm ?? DefaultRadiusKm) < 100)
        {
            options.Add(("widen_radius", "Search a wider area (up to 100 km).", query with { RadiusKm = 100 }));
        }

        if (query.From is not null || query.To is not null)
        {
            options.Add(("any_time", "Try any time on the same day.", query with { From = null, To = null }));
        }

        if (query.Date is not null)
        {
            options.Add(("any_date", "Try without a date.", query with { Date = null, From = null, To = null }));
        }

        if (query.MaxPrice is not null)
        {
            options.Add(("any_budget", "Remove the budget limit.", query with { MaxPrice = null }));
        }

        if (query.PitchType is not null || query.Surface is not null)
        {
            options.Add(("any_pitch", "Try any pitch size and surface.", query with { PitchType = null, Surface = null }));
        }

        if (query.Facilities is { Count: > 0 })
        {
            options.Add(("fewer_facilities", "Remove the facility filters.", query with { Facilities = null }));
        }

        if (query.City is not null || query.Area is not null)
        {
            options.Add(("any_area", "Search all areas.", query with { City = null, Area = null }));
        }

        var suggestions = new List<ExploreSuggestion>();
        foreach (var (code, message, relaxed) in options)
        {
            var count = (await FilterAsync(relaxed, cancellationToken)).Count;
            if (count > 0)
            {
                suggestions.Add(new ExploreSuggestion(code, message, count));
            }
        }

        return suggestions.OrderByDescending(s => s.MatchCount).ToList();
    }

    private static bool InTimeRange(TimeOnly time, TimeOnly? from, TimeOnly? to)
    {
        if (from is null && to is null)
        {
            return true;
        }

        var start = from ?? TimeOnly.MinValue;
        var end = to ?? TimeOnly.MaxValue;
        return end >= start ? time >= start && time <= end : time >= start || time <= end;
    }

    private static IEnumerable<ExploreVenueCard> Sort(IEnumerable<ExploreVenueCard> cards, ExploreQuery query)
    {
        var sort = query.Sort == ExploreSort.Distance && query.Latitude is null ? ExploreSort.Popularity : query.Sort;
        return sort switch
        {
            ExploreSort.Distance => cards.OrderBy(c => c.DistanceKm ?? double.MaxValue).ThenBy(c => c.VenueId),
            ExploreSort.Price => cards.OrderBy(c => c.FromHourlyPrice ?? decimal.MaxValue).ThenBy(c => c.VenueId),
            ExploreSort.Name => cards.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.VenueId),
            _ => cards.OrderByDescending(c => c.FavoritesCount).ThenBy(c => c.VenueId)
        };
    }

    /// <summary>Lowest base hourly rate among the matching pitches (pitch override, else venue default).</summary>
    private static decimal? FromPrice(VenueCandidate venue, List<PitchCandidate> pitches)
    {
        var venueDefault = venue.BaseRates.Where(r => r.PitchId is null).Select(r => (decimal?)r.Amount).Min();
        return pitches
            .Select(p => venue.BaseRates.Where(r => r.PitchId == p.Id).Select(r => (decimal?)r.Amount).Min() ?? venueDefault)
            .Where(price => price is not null)
            .Min();
    }

    private static Error? Validate(ExploreQuery q)
    {
        var coordinatesOk = (q.Latitude is null) == (q.Longitude is null)
            && (q.Latitude is null || (q.Latitude is >= -90 and <= 90 && q.Longitude is >= -180 and <= 180));
        if (!coordinatesOk || q.RadiusKm is < 1 or > 100 || q.Page < 1 || q.PageSize is < 1 or > 50
            || ((q.From is not null || q.To is not null) && q.Date is null) || q.MaxPrice is <= 0 || !Enum.IsDefined(q.Sort)
            || (q.Facilities?.Count ?? 0) > 20 || !BoundsOk(q.Bounds))
        {
            return ExploreErrors.InvalidQuery;
        }

        var knownPitchType = q.PitchType is null || EgyptReferenceData.PitchTypes.Any(t => string.Equals(t.Code, q.PitchType, StringComparison.OrdinalIgnoreCase));
        var knownSurface = q.Surface is null || EgyptReferenceData.Surfaces.Any(s => string.Equals(s.Code, q.Surface, StringComparison.OrdinalIgnoreCase));
        var knownFacilities = (q.Facilities ?? []).All(EgyptReferenceData.IsKnownFacility);
        return knownPitchType && knownSurface && knownFacilities ? null : ExploreErrors.UnknownCode;
    }

    private static bool BoundsOk(MapBounds? b) =>
        b is null
        || (b.South is >= -90 and <= 90 && b.North is >= -90 and <= 90 && b.West is >= -180 and <= 180 && b.East is >= -180 and <= 180
            && b.South < b.North && b.West < b.East
            && b.North - b.South <= MaxBoundsSpanDegrees && b.East - b.West <= MaxBoundsSpanDegrees);

    private static int? PitchCapacityFor(string? pitchType) =>
        pitchType is null ? null : int.Parse(PitchTypeRegex().Match(pitchType).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Accepted stored spellings (code, English, Arabic; lower-cased) for the city and area filters.</summary>
    private static (List<string>? Cities, List<string>? Areas) PlaceVariants(string? city, string? area)
    {
        var governorate = EgyptReferenceData.FindGovernorate(city);
        List<string>? cities = null;
        if (governorate is not null)
        {
            cities = [governorate.Code.ToLowerInvariant(), governorate.NameEn.ToLowerInvariant(), governorate.NameAr];
        }
        else if (!string.IsNullOrWhiteSpace(city))
        {
            cities = [city.Trim().ToLowerInvariant()];
        }
        if (string.IsNullOrWhiteSpace(area))
        {
            return (cities, null);
        }

        var key = area.Trim();
        var curated = governorate?.Areas.FirstOrDefault(a =>
            string.Equals(a.Code, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.NameEn, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.NameAr, key, StringComparison.Ordinal));
        List<string> areas = curated is null
            ? [key.ToLowerInvariant()]
            : [curated.Code.ToLowerInvariant(), curated.NameEn.ToLowerInvariant(), curated.NameAr];
        return (cities, areas);
    }

    private static string FacilityCode(string value) =>
        (EgyptReferenceData.Facilities.FirstOrDefault(f =>
            string.Equals(f.Code, value.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.NameEn, value.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.NameAr, value.Trim(), StringComparison.Ordinal))?.Code ?? value.Trim()).ToLowerInvariant();

    [GeneratedRegex("^(\\d+)v\\d+$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex PitchTypeRegex();

    private sealed record PitchCandidate(Guid Id, Guid VenueId, int Capacity, string TurfType);

    private sealed record VenueCandidate(
        Guid Id, string Name, string Slug, string City, string Area, double? Latitude, double? Longitude,
        IReadOnlyCollection<string> Facilities, IReadOnlyCollection<string> Photos, List<PitchCandidate> Pitches,
        List<(Guid? PitchId, decimal Amount)> BaseRates, string Currency, int FavoritesCount);
}

/// <summary>Great-circle distance and bounding boxes (WGS84 sphere approximation, accurate to well under 1% for city distances).</summary>
public static class GeoMath
{
    private const double _earthRadiusKm = 6371.0088;

    public static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLng = ToRadians(lng2 - lng1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + (Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Pow(Math.Sin(dLng / 2), 2));
        return 2 * _earthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    public static (double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude) BoundingBox(double lat, double lng, double radiusKm)
    {
        var dLat = radiusKm / 111.32;
        var dLng = radiusKm / (111.32 * Math.Max(0.01, Math.Cos(ToRadians(lat))));
        return (lat - dLat, lat + dLat, lng - dLng, lng + dLng);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
