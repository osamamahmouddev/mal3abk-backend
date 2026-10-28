namespace Malabak.Modules.Venues.Application.Common;

/// <summary>
/// Per-request venue scope used by the VenuesDbContext global query filters. Once restricted, every
/// venue-owned row outside <see cref="VenueIds"/> is invisible to queries in this request, as a second
/// line of defence behind the handlers' own access checks.
/// </summary>
public interface IVenueTenantContext
{
    bool IsRestricted { get; }

    IReadOnlyCollection<Guid> VenueIds { get; }

    void RestrictTo(IEnumerable<Guid> venueIds);
}
