using Malabak.Modules.Venues.Application.Explore;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Venues.Application.Home;

public static class HomeSectionStatus
{
    public const string Ok = "ok";
    public const string Empty = "empty";
    public const string LocationRequired = "location_required";
    public const string SignInRequired = "sign_in_required";
    public const string Unavailable = "unavailable";
    public const string ComingSoon = "coming_soon";
}

/// <summary>One home feed block. A failing block reports <c>unavailable</c> instead of failing the whole feed.</summary>
public sealed record HomeSection<T>(string Status, IReadOnlyList<T> Items)
{
    public static HomeSection<T> Of(IReadOnlyList<T> items) => new(items.Count == 0 ? HomeSectionStatus.Empty : HomeSectionStatus.Ok, items);

    public static HomeSection<T> With(string status) => new(status, []);
}

/// <summary>A paid placement. Always labeled so players can tell it apart from organic results.</summary>
public sealed record SponsoredItem(string Label, string Title, ExploreVenueCard Venue);

public sealed record HomeFeedDto(
    HomeSection<ExploreVenueCard> Nearby,
    HomeSection<ExploreVenueCard> PopularNearYou,
    HomeSection<ExploreVenueCard> RecentlyViewed,
    HomeSection<object> UpcomingBooking,
    HomeSection<object> TeamActivity,
    HomeSection<SponsoredItem>? Sponsored,
    DateTime GeneratedAtUtc);

/// <summary>Home feed switches (host configuration, <c>Features:SponsoredHomeItems</c>).</summary>
public sealed record HomeFeedSettings(bool SponsoredItemsEnabled);

public sealed record HomeFeedQuery(double? Latitude = null, double? Longitude = null, string? City = null, string? Area = null);

/// <summary>
/// Home feed (TASK-304): one call for the player's home screen. Nearby and popular share a single venue search (and the
/// cached availability behind it); each section is isolated so one failing source degrades only its own block. The
/// upcoming booking and team activity blocks are placeholders until bookings (TASK-401) and teams ship; sponsored items
/// are behind a feature flag and have no source yet.
/// </summary>
public sealed class HomeFeed
{
    public const int ItemsPerSection = 10;
    public const double NearbyRadiusKm = 25;

    private readonly VenueExplore _explore;
    private readonly VenuesDbContext _dbContext;
    private readonly HomeFeedSettings _settings;
    private readonly IClock _clock;
    private readonly ILogger<HomeFeed> _logger;

    public HomeFeed(VenueExplore explore, VenuesDbContext dbContext, HomeFeedSettings settings, IClock clock, ILogger<HomeFeed> logger)
    {
        _explore = explore;
        _dbContext = dbContext;
        _settings = settings;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<HomeFeedDto>> GetAsync(HomeFeedQuery query, Guid? userId, CancellationToken cancellationToken = default)
    {
        var hasLocation = query.Latitude is not null && query.Longitude is not null;
        if ((query.Latitude is null) != (query.Longitude is null)
            || query.Latitude is < -90 or > 90 || query.Longitude is < -180 or > 180)
        {
            return Result.Failure<HomeFeedDto>(ExploreErrors.InvalidQuery);
        }

        var (nearby, popular) = await GuardAsync("nearby", () => NearbyAndPopularAsync(query, hasLocation, userId, cancellationToken),
            (HomeSection<ExploreVenueCard>.With(HomeSectionStatus.Unavailable), HomeSection<ExploreVenueCard>.With(HomeSectionStatus.Unavailable)));
        var recent = userId is { } user
            ? await GuardAsync("recently_viewed", () => RecentlyViewedAsync(user, cancellationToken), HomeSection<ExploreVenueCard>.With(HomeSectionStatus.Unavailable))
            : HomeSection<ExploreVenueCard>.With(HomeSectionStatus.SignInRequired);

        return Result.Success(new HomeFeedDto(
            nearby, popular, recent,
            HomeSection<object>.With(userId is null ? HomeSectionStatus.SignInRequired : HomeSectionStatus.ComingSoon),
            HomeSection<object>.With(HomeSectionStatus.ComingSoon),
            _settings.SponsoredItemsEnabled ? HomeSection<SponsoredItem>.Of([]) : null,
            _clock.UtcNow));
    }

    private async Task<(HomeSection<ExploreVenueCard> Nearby, HomeSection<ExploreVenueCard> Popular)> NearbyAndPopularAsync(
        HomeFeedQuery query, bool hasLocation, Guid? userId, CancellationToken cancellationToken)
    {
        var hasArea = !string.IsNullOrWhiteSpace(query.City) || !string.IsNullOrWhiteSpace(query.Area);
        var filter = hasLocation
            ? new ExploreQuery(query.Latitude, query.Longitude, NearbyRadiusKm)
            : new ExploreQuery(City: query.City, Area: query.Area);
        var ranked = await _explore.RankAsync(filter, [ExploreSort.Distance, ExploreSort.Popularity], ItemsPerSection, userId, cancellationToken);

        // Without location or a chosen area there is no "near you": popular falls back to the most favorited overall.
        var nearby = hasLocation || hasArea ? HomeSection<ExploreVenueCard>.Of(ranked[0]) : HomeSection<ExploreVenueCard>.With(HomeSectionStatus.LocationRequired);
        var popular = HomeSection<ExploreVenueCard>.Of(ranked[1].Where(v => v.FavoritesCount > 0).ToList());
        return (nearby, popular);
    }

    private async Task<HomeSection<ExploreVenueCard>> RecentlyViewedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var recent = await _dbContext.RecentlyViewedVenues.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.ViewedAtUtc)
            .Take(ItemsPerSection)
            .Select(r => r.VenueId)
            .ToListAsync(cancellationToken);
        if (recent.Count == 0)
        {
            return HomeSection<ExploreVenueCard>.With(HomeSectionStatus.Empty);
        }

        // Venues that were unpublished since the visit simply drop out.
        var cards = (await _explore.RankAsync(new ExploreQuery(VenueIds: recent), [ExploreSort.Name], ItemsPerSection, userId, cancellationToken))[0];
        return HomeSection<ExploreVenueCard>.Of(cards.OrderBy(c => recent.IndexOf(c.VenueId)).ToList());
    }

    private async Task<T> GuardAsync<T>(string section, Func<Task<T>> load, T fallback)
    {
        try
        {
            return await load();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Home feed section {Section} failed; returning it as unavailable", section);
            return fallback;
        }
    }
}
