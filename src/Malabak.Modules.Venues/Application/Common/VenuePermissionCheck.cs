using Malabak.Modules.Venues.Infrastructure;
using Malabak.Modules.Venues.Infrastructure.Services;

namespace Malabak.Modules.Venues.Application.Common;

/// <summary>Resource-based check used by write handlers: owner or a staff role that grants <c>permission</c> on this venue.</summary>
public static class VenuePermissionCheck
{
    public static async Task<bool> CanAsync(VenuesDbContext dbContext, Guid userId, Guid venueId, string permission, CancellationToken cancellationToken)
    {
        var access = await new VenueAccessService(dbContext).GetAccessAsync(userId, venueId, cancellationToken);
        return access.Can(permission);
    }
}
