using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Infrastructure.Repositories;

public sealed class BlockedSlotRepository : IBlockedSlotRepository
{
    private readonly VenuesDbContext _dbContext;

    public BlockedSlotRepository(VenuesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BlockedSlot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.BlockedSlots
            .FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<BlockedSlot>> GetByVenueIdAsync(Guid venueId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.BlockedSlots
            .Where(b => b.VenueId == venueId && !b.IsDeleted)
            .OrderBy(b => b.StartUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BlockedSlot>> GetByPitchIdAsync(Guid pitchId, DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.BlockedSlots
            .Where(b => b.PitchId == pitchId && !b.IsDeleted);

        if (fromUtc.HasValue)
        {
            query = query.Where(b => b.EndUtc > fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(b => b.StartUtc < toUtc.Value);
        }

        return await query.OrderBy(b => b.StartUtc).ToListAsync(cancellationToken);
    }

    public async Task<bool> HasOverlappingBlockAsync(Guid pitchId, DateTime startUtc, DateTime endUtc, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        return await _dbContext.BlockedSlots
            .AnyAsync(b => b.PitchId == pitchId
                && !b.IsDeleted
                && (!excludeId.HasValue || b.Id != excludeId.Value)
                && b.StartUtc < endUtc
                && b.EndUtc > startUtc,
                cancellationToken);
    }

    public void Add(BlockedSlot blockedSlot)
    {
        _dbContext.BlockedSlots.Add(blockedSlot);
    }
}
