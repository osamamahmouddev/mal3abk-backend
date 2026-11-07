using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Domain;

/// <summary>Where a featured venue is shown.</summary>
public enum FeaturedPlacementSlot
{
    /// <summary>The sponsored block of the player home feed.</summary>
    Home = 0,

    /// <summary>The featured strip at the top of venue explore.</summary>
    Explore = 1
}

/// <summary>
/// A paid or promotional slot for a venue (TASK-711), scheduled by an admin. Clients must always show it with the sponsored
/// label: the API returns the label with every featured item so it cannot be dropped silently.
/// </summary>
public sealed class FeaturedPlacement
{
    public const int MaxTitleLength = 80;
    public const int MaxDurationDays = 90;
    public const string SponsoredLabel = "sponsored";

    private FeaturedPlacement()
    {
    }

    public Guid Id { get; private set; }
    public Guid VenueId { get; private set; }
    public FeaturedPlacementSlot Slot { get; private set; }
    public string? Title { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public int SortOrder { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }

    public bool IsLiveAt(DateTime nowUtc) => CancelledAtUtc is null && StartsAtUtc <= nowUtc && nowUtc < EndsAtUtc;

    public static Result<FeaturedPlacement> Schedule(
        Guid venueId, FeaturedPlacementSlot slot, string? title, DateTime startsAtUtc, DateTime endsAtUtc, int sortOrder, Guid adminId, DateTime nowUtc)
    {
        var placement = new FeaturedPlacement { Id = Guid.CreateVersion7(), VenueId = venueId, CreatedBy = adminId, CreatedAtUtc = nowUtc };
        var applied = placement.Change(slot, title, startsAtUtc, endsAtUtc, sortOrder, nowUtc);
        return applied.IsFailure ? Result.Failure<FeaturedPlacement>(applied.Error) : Result.Success(placement);
    }

    public Result Change(FeaturedPlacementSlot slot, string? title, DateTime startsAtUtc, DateTime endsAtUtc, int sortOrder, DateTime nowUtc)
    {
        if (CancelledAtUtc is not null)
        {
            return Result.Failure(FeaturedErrors.Cancelled);
        }

        if (!Enum.IsDefined(slot) || title?.Trim().Length > MaxTitleLength || sortOrder is < 0 or > 1000)
        {
            return Result.Failure(FeaturedErrors.Invalid);
        }

        if (endsAtUtc <= startsAtUtc || endsAtUtc <= nowUtc || endsAtUtc - startsAtUtc > TimeSpan.FromDays(MaxDurationDays))
        {
            return Result.Failure(FeaturedErrors.InvalidSchedule);
        }

        Slot = slot;
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        SortOrder = sortOrder;
        return Result.Success();
    }

    public Result Cancel(DateTime nowUtc)
    {
        if (CancelledAtUtc is not null)
        {
            return Result.Failure(FeaturedErrors.Cancelled);
        }

        CancelledAtUtc = nowUtc;
        return Result.Success();
    }
}

public static class FeaturedErrors
{
    public static readonly Error Invalid = Error.Validation("Featured.Invalid", "Choose a placement, a title of up to 80 characters and a sort order from 0 to 1000.");
    public static readonly Error InvalidSchedule = Error.Validation("Featured.InvalidSchedule",
        "The end must be after the start and in the future, and a placement can run for at most 90 days.");
    public static readonly Error VenueNotLive = Error.Validation("Featured.VenueNotLive", "Only approved, published venues can be featured.");
    public static readonly Error NotFound = Error.NotFound("Featured.NotFound", "Featured placement not found.");
    public static readonly Error Cancelled = Error.Conflict("Featured.Cancelled", "This placement was cancelled.");
    public static readonly Error SlotFull = Error.Conflict("Featured.SlotFull", "Too many placements already run in this slot at that time.");
}
