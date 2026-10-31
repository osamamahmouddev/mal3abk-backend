using Malabak.SharedKernel.Contracts;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>A fixed answer to the venue questions booking handlers ask, for tests that do not load the Venues module.</summary>
internal sealed class FakeVenueDirectory : IVenueDirectory
{
    public const string VenueName = "Test Venue";

    private readonly IReadOnlyList<Guid> _staff;

    private readonly Dictionary<Guid, Guid> _pitchVenues = [];

    public FakeVenueDirectory(params Guid[] staff) => _staff = staff;

    /// <summary>Declares that the pitch belongs to the venue.</summary>
    public FakeVenueDirectory WithPitch(Guid pitchId, Guid venueId)
    {
        _pitchVenues[pitchId] = venueId;
        return this;
    }

    public Task<Guid?> GetVenueIdOfPitchAsync(Guid pitchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_pitchVenues.TryGetValue(pitchId, out var venueId) ? venueId : (Guid?)null);

    public Task<IReadOnlyDictionary<Guid, string>> GetVenueNamesAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(venueIds.Distinct().ToDictionary(id => id, _ => VenueName));

    public Task<IReadOnlyList<Guid>> GetStaffUserIdsAsync(Guid venueId, string permission, CancellationToken cancellationToken = default) => Task.FromResult(_staff);
}
