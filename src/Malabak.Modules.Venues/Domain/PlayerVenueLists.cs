namespace Malabak.Modules.Venues.Domain;

/// <summary>A player's favorite venue. Private to the player: never exposed to venue staff.</summary>
public sealed class FavoriteVenue
{
    public const int MaxPerUser = 200;

    private FavoriteVenue()
    {
    }

    public Guid UserId { get; private set; }
    public Guid VenueId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static FavoriteVenue Create(Guid userId, Guid venueId, DateTime nowUtc) =>
        new() { UserId = userId, VenueId = venueId, CreatedAtUtc = nowUtc };
}

/// <summary>Bounded per-player history of venue detail views (oldest entries are dropped).</summary>
public sealed class RecentlyViewedVenue
{
    public const int MaxPerUser = 20;

    private RecentlyViewedVenue()
    {
    }

    public Guid UserId { get; private set; }
    public Guid VenueId { get; private set; }
    public DateTime ViewedAtUtc { get; private set; }

    public static RecentlyViewedVenue Create(Guid userId, Guid venueId, DateTime nowUtc) =>
        new() { UserId = userId, VenueId = venueId, ViewedAtUtc = nowUtc };

    public void Touch(DateTime nowUtc) => ViewedAtUtc = nowUtc;
}
