using Malabak.Modules.Venues.Domain;

namespace Malabak.Modules.Venues.Application.Common;

public interface IPitchRepository
{
    Task<Pitch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Pitch>> GetByVenueIdAsync(Guid venueId, CancellationToken cancellationToken = default);

    void Add(Pitch pitch);

    void Delete(Pitch pitch);
}
