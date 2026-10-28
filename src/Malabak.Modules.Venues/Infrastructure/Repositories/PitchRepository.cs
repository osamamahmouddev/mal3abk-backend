using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Infrastructure.Repositories;

public sealed class PitchRepository : IPitchRepository
{
    private readonly VenuesDbContext _dbContext;

    public PitchRepository(VenuesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Pitch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Pitches
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<Pitch>> GetByVenueIdAsync(Guid venueId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Pitches
            .Where(p => p.VenueId == venueId && !p.IsDeleted)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public void Add(Pitch pitch)
    {
        _dbContext.Pitches.Add(pitch);
    }

    public void Delete(Pitch pitch)
    {
        pitch.MarkDeleted(DateTime.UtcNow);
    }
}
