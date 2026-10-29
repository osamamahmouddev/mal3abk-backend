namespace Malabak.Modules.Venues.Application.Common;

/// <summary>
/// Resource-based authorization for venues: "is this user the owner (or a staff member) of this venue,
/// and what may they do there?" Always answered from the database, never from client-supplied ids alone.
/// </summary>
public interface IVenueAccessService
{
    Task<VenueAccess> GetAccessAsync(Guid userId, Guid venueId, CancellationToken cancellationToken = default);

    /// <summary>Every non-deleted venue the user can act on, with the access they have there.</summary>
    Task<IReadOnlyList<VenueAccess>> GetAccessibleVenuesAsync(Guid userId, CancellationToken cancellationToken = default);
}
