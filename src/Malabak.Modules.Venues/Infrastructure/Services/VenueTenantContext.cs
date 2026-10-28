using Malabak.Modules.Venues.Application.Common;

namespace Malabak.Modules.Venues.Infrastructure.Services;

public sealed class VenueTenantContext : IVenueTenantContext
{
    private Guid[] _venueIds = [];

    public bool IsRestricted { get; private set; }

    public IReadOnlyCollection<Guid> VenueIds => _venueIds;

    public void RestrictTo(IEnumerable<Guid> venueIds)
    {
        ArgumentNullException.ThrowIfNull(venueIds);

        // Once restricted, a request can only narrow its scope, never widen or lift it.
        var requested = venueIds.Distinct().ToArray();
        _venueIds = IsRestricted ? _venueIds.Intersect(requested).ToArray() : requested;
        IsRestricted = true;
    }
}
