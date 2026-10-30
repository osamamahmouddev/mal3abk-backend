using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Api.Infrastructure.Media;

/// <summary>
/// Uploaded media in the personal data export. On account erasure the avatar is shredded from storage (ADR-0007);
/// payment proofs follow their own retention (booking evidence) and venue photos belong to the venue.
/// </summary>
public sealed class MediaPersonalData : IPersonalDataContributor
{
    private readonly MediaDbContext _dbContext;
    private readonly MediaService _media;

    public MediaPersonalData(MediaDbContext dbContext, MediaService media)
    {
        _dbContext = dbContext;
        _media = media;
    }

    public string Section => "media";

    public async Task<object?> ExportAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var assets = await _dbContext.Assets.AsNoTracking()
            .Where(a => a.OwnerUserId == userId && a.Status == MediaStatus.Ready)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return assets.Select(a => new { a.Id, Purpose = a.Purpose.ToString(), a.CreatedAtUtc, Urls = _media.UrlsFor(a) }).ToList();
    }

    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var avatars = await _dbContext.Assets.Where(a => a.OwnerUserId == userId && a.Purpose == MediaPurpose.Avatar).ToListAsync(cancellationToken);
        foreach (var avatar in avatars)
        {
            await _media.DeleteAsync(avatar, cancellationToken);
        }
    }
}
