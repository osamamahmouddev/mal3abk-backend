using Malabak.Modules.Venues.Domain;

namespace Malabak.Modules.Venues.Application.Common;

public interface IVenueRepository
{
    Task<Venue?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Venue?> GetByIdWithPitchesAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Venue?> GetByIdWithSchedulesAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Venue>> GetByOwnerIdAsync(Guid ownerId, CancellationToken cancellationToken = default);

    Task<bool> ExistsBySlugAsync(string slug, Guid? excludeVenueId = null, CancellationToken cancellationToken = default);

    void Add(Venue venue);

    void Delete(Venue venue);
}
