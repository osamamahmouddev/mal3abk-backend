namespace Malabak.SharedKernel.Contracts;

/// <summary>What a booking screen shows about the venue, including where it is for directions.</summary>
public sealed record VenueSummary(Guid Id, string Name, string Address, double? Latitude, double? Longitude);

/// <summary>A period the venue closed a pitch (maintenance, private event), for the manager's calendar.</summary>
public sealed record VenueBlockedPeriod(Guid Id, Guid PitchId, DateTime StartUtc, DateTime EndUtc, string Reason);

/// <summary>The commission settings configured for a venue.</summary>
public sealed record VenueCommissionInfo(Guid VenueId, bool CustomCommissionEnabled, string CommissionType, decimal? CommissionRate);

/// <summary>
/// Venues-module contract for the questions other modules (Booking) must ask about venues without reading Venues' tables:
/// which venue a pitch belongs to, what a venue is called, and who works there. Answers come from the database, never from ids a
/// client sent, and ignore the request's tenant scope: a player's booking must still reach the venue's staff.
/// </summary>
public interface IVenueDirectory
{
    /// <summary>The venue the pitch belongs to, or null when the pitch does not exist or was deleted.</summary>
    Task<Guid?> GetVenueIdOfPitchAsync(Guid pitchId, CancellationToken cancellationToken = default);

    /// <summary>The venue's name, address and coordinates, or null when it does not exist or was deleted.</summary>
    Task<VenueSummary?> GetVenueSummaryAsync(Guid venueId, CancellationToken cancellationToken = default);

    /// <summary>The venue's blocked periods that overlap the range, optionally for one pitch.</summary>
    Task<IReadOnlyList<VenueBlockedPeriod>> GetBlockedPeriodsAsync(Guid venueId, DateTime fromUtc, DateTime toUtc, Guid? pitchId, CancellationToken cancellationToken = default);

    /// <summary>Venue names by id, for notification text. Venues that do not exist are left out.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetVenueNamesAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default);

    /// <summary>Each venue's first gallery photo by id, for booking lists. Venues without photos are left out.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetVenueCoverPhotosAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default);

    /// <summary>Pitch names by id, for booking lists. Pitches that do not exist are left out.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetPitchNamesAsync(IReadOnlyCollection<Guid> pitchIds, CancellationToken cancellationToken = default);

    /// <summary>Commission settings for specified venues.</summary>
    Task<IReadOnlyDictionary<Guid, VenueCommissionInfo>> GetVenueCommissionsAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, VenueCommissionInfo>>(new Dictionary<Guid, VenueCommissionInfo>());

    /// <summary>
    /// The users who may act on the venue with the given permission (see <c>Permissions</c>): its owner, who may do everything
    /// there, and the staff whose role includes it.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetStaffUserIdsAsync(Guid venueId, string permission, CancellationToken cancellationToken = default);
}
