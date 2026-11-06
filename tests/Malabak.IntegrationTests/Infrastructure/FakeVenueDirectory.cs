using Malabak.SharedKernel.Contracts;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>A fixed answer to the venue questions booking handlers ask, for tests that do not load the Venues module.</summary>
internal sealed class FakeVenueDirectory : IVenueDirectory
{
    public const string VenueName = "Test Venue";
    public const string PitchName = "Pitch 1";

    private readonly IReadOnlyList<Guid> _staff;

    private readonly Dictionary<Guid, Guid> _pitchVenues = [];
    private readonly List<VenueBlockedPeriod> _blocked = [];

    public FakeVenueDirectory(params Guid[] staff) => _staff = staff;

    /// <summary>Declares that the pitch belongs to the venue.</summary>
    public FakeVenueDirectory WithPitch(Guid pitchId, Guid venueId)
    {
        _pitchVenues[pitchId] = venueId;
        return this;
    }

    public Task<Guid?> GetVenueIdOfPitchAsync(Guid pitchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_pitchVenues.TryGetValue(pitchId, out var venueId) ? venueId : (Guid?)null);

    public Task<VenueSummary?> GetVenueSummaryAsync(Guid venueId, CancellationToken cancellationToken = default) =>
        Task.FromResult<VenueSummary?>(new VenueSummary(venueId, VenueName, "1 Test Street", 30.0, 31.0));

    public FakeVenueDirectory WithBlocked(VenueBlockedPeriod period)
    {
        _blocked.Add(period);
        return this;
    }

    public Task<IReadOnlyList<VenueBlockedPeriod>> GetBlockedPeriodsAsync(Guid venueId, DateTime fromUtc, DateTime toUtc, Guid? pitchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<VenueBlockedPeriod>>(_blocked.Where(b => b.StartUtc < toUtc && b.EndUtc > fromUtc && (pitchId is null || b.PitchId == pitchId)).ToList());

    public Task<IReadOnlyDictionary<Guid, string>> GetVenueNamesAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(venueIds.Distinct().ToDictionary(id => id, _ => VenueName));

    public Task<IReadOnlyDictionary<Guid, string>> GetPitchNamesAsync(IReadOnlyCollection<Guid> pitchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(pitchIds.Distinct().ToDictionary(id => id, _ => PitchName));

    public Task<IReadOnlyList<Guid>> GetStaffUserIdsAsync(Guid venueId, string permission, CancellationToken cancellationToken = default) => Task.FromResult(_staff);
}
