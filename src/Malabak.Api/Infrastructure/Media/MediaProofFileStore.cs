using Malabak.Modules.Booking.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Api.Infrastructure.Media;

/// <summary>Lets the booking module's retention job delete a payment proof's image from the media store (ADR-0007).</summary>
public sealed class MediaProofFileStore : IProofFileStore
{
    private readonly MediaDbContext _dbContext;
    private readonly MediaService _media;

    public MediaProofFileStore(MediaDbContext dbContext, MediaService media)
    {
        _dbContext = dbContext;
        _media = media;
    }

    public async Task DeleteAsync(Guid mediaId, CancellationToken cancellationToken = default)
    {
        var asset = await _dbContext.Assets.FirstOrDefaultAsync(a => a.Id == mediaId, cancellationToken);
        if (asset is null)
        {
            return;
        }

        await _media.DeleteAsync(asset, cancellationToken);
    }
}
