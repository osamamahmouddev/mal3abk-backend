namespace Malabak.SharedKernel.Contracts;

/// <summary>
/// Venues-module contract for the questions other modules (Booking) must ask about venues without reading Venues' tables:
/// which venue a pitch belongs to, what a venue is called, and who works there. Answers come from the database, never from ids a
/// client sent, and ignore the request's tenant scope: a player's booking must still reach the venue's staff.
/// </summary>
public interface IVenueDirectory
{
    /// <summary>The venue the pitch belongs to, or null when the pitch does not exist or was deleted.</summary>
    Task<Guid?> GetVenueIdOfPitchAsync(Guid pitchId, CancellationToken cancellationToken = default);

    /// <summary>Venue names by id, for notification text. Venues that do not exist are left out.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetVenueNamesAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// The users who may act on the venue with the given permission (see <c>Permissions</c>): its owner, who may do everything
    /// there, and the staff whose role includes it.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetStaffUserIdsAsync(Guid venueId, string permission, CancellationToken cancellationToken = default);
}
