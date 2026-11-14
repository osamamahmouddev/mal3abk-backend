namespace Malabak.SharedKernel.Contracts;

public sealed record OwnerVenueInfo(Guid Id, string Name, string City, bool IsPublished);

/// <summary>What a venue owner currently uses, for plan limits and the billing page.</summary>
public sealed record OwnerUsage(int Venues, int Pitches, int MaxStaffInOneVenue);

/// <summary>Venues-module contract: current usage of one owner (non-deleted venues and pitches, active staff).</summary>
public interface IOwnerUsageReader
{
    Task<OwnerUsage> GetAsync(Guid ownerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<OwnerVenueInfo>>> GetOwnerVenuesBatchAsync(IReadOnlyCollection<Guid> ownerIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<OwnerVenueInfo>>>(new Dictionary<Guid, IReadOnlyList<OwnerVenueInfo>>());
}
