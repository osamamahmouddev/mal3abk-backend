using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.PlayerLists;

/// <summary>Favorites and recently viewed venues in the personal data export, and removed on account deletion.</summary>
public sealed class PlayerListsPersonalData : IPersonalDataContributor
{
    private readonly VenuesDbContext _dbContext;

    public PlayerListsPersonalData(VenuesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Section => "venues";

    public async Task<object?> ExportAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var favorites = await _dbContext.FavoriteVenues.AsNoTracking().Where(f => f.UserId == userId)
            .Select(f => new { f.VenueId, f.CreatedAtUtc }).ToListAsync(cancellationToken);
        var recentlyViewed = await _dbContext.RecentlyViewedVenues.AsNoTracking().Where(r => r.UserId == userId)
            .Select(r => new { r.VenueId, r.ViewedAtUtc }).ToListAsync(cancellationToken);
        return new { Favorites = favorites, RecentlyViewed = recentlyViewed };
    }

    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        _dbContext.FavoriteVenues.RemoveRange(await _dbContext.FavoriteVenues.Where(f => f.UserId == userId).ToListAsync(cancellationToken));
        _dbContext.RecentlyViewedVenues.RemoveRange(await _dbContext.RecentlyViewedVenues.Where(r => r.UserId == userId).ToListAsync(cancellationToken));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
