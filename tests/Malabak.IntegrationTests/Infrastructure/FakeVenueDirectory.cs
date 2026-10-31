using Malabak.SharedKernel.Contracts;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>A fixed answer to the venue questions booking handlers ask, for tests that do not load the Venues module.</summary>
internal sealed class FakeVenueDirectory : IVenueDirectory
{
    public const string VenueName = "Test Venue";

    private readonly IReadOnlyList<Guid> _staff;

    public FakeVenueDirectory(params Guid[] staff) => _staff = staff;

    public Task<Guid?> GetVenueIdOfPitchAsync(Guid pitchId, CancellationToken cancellationToken = default) => Task.FromResult<Guid?>(null);

    public Task<IReadOnlyDictionary<Guid, string>> GetVenueNamesAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(venueIds.Distinct().ToDictionary(id => id, _ => VenueName));

    public Task<IReadOnlyList<Guid>> GetStaffUserIdsAsync(Guid venueId, string permission, CancellationToken cancellationToken = default) => Task.FromResult(_staff);
}
