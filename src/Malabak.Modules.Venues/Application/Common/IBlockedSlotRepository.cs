using Malabak.Modules.Venues.Domain;

namespace Malabak.Modules.Venues.Application.Common;

public interface IBlockedSlotRepository
{
    Task<BlockedSlot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BlockedSlot>> GetByVenueIdAsync(Guid venueId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BlockedSlot>> GetByPitchIdAsync(Guid pitchId, DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default);

    Task<bool> HasOverlappingBlockAsync(Guid pitchId, DateTime startUtc, DateTime endUtc, Guid? excludeId = null, CancellationToken cancellationToken = default);

    void Add(BlockedSlot blockedSlot);
}
