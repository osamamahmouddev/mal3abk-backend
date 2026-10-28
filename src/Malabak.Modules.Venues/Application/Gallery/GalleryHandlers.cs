using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Gallery;

/// <param name="PitchId">Null for the venue gallery, otherwise the pitch's gallery.</param>
public sealed record AddGalleryPhotoCommand(Guid VenueId, Guid? PitchId, Guid UserId, string PhotoUrl) : ICommand<Result<IReadOnlyList<string>>>;

public sealed record RemoveGalleryPhotoCommand(Guid VenueId, Guid? PitchId, Guid UserId, string PhotoUrl) : ICommand<Result<IReadOnlyList<string>>>;

public static class GalleryErrors
{
    public static readonly Error Full = new("Gallery.Full", "The gallery already has the maximum number of photos (20) or this photo.");
    public static readonly Error PhotoNotFound = new("Gallery.PhotoNotFound", "Photo not found in this gallery.");
}

/// <summary>
/// Venue and pitch galleries. The photo URL comes from the media pipeline (validated and re-encoded, owned by the
/// caller); this handler only checks venue permissions and gallery limits.
/// </summary>
public sealed class GalleryHandlers :
    ICommandHandler<AddGalleryPhotoCommand, Result<IReadOnlyList<string>>>,
    ICommandHandler<RemoveGalleryPhotoCommand, Result<IReadOnlyList<string>>>
{
    private readonly VenuesDbContext _dbContext;
    private readonly IVenueAccessService _access;
    private readonly IClock _clock;

    public GalleryHandlers(VenuesDbContext dbContext, IVenueAccessService access, IClock clock)
    {
        _dbContext = dbContext;
        _access = access;
        _clock = clock;
    }

    public Task<Result<IReadOnlyList<string>>> HandleAsync(AddGalleryPhotoCommand command, CancellationToken cancellationToken = default) =>
        MutateAsync(command.VenueId, command.PitchId, command.UserId, cancellationToken,
            onVenue: v => v.AddPhoto(command.PhotoUrl, _clock.UtcNow), onPitch: p => p.AddPhoto(command.PhotoUrl, _clock.UtcNow), GalleryErrors.Full);

    public Task<Result<IReadOnlyList<string>>> HandleAsync(RemoveGalleryPhotoCommand command, CancellationToken cancellationToken = default) =>
        MutateAsync(command.VenueId, command.PitchId, command.UserId, cancellationToken,
            onVenue: v => v.RemovePhoto(command.PhotoUrl, _clock.UtcNow), onPitch: p => p.RemovePhoto(command.PhotoUrl, _clock.UtcNow), GalleryErrors.PhotoNotFound);

    private async Task<Result<IReadOnlyList<string>>> MutateAsync(
        Guid venueId, Guid? pitchId, Guid userId, CancellationToken cancellationToken,
        Func<Domain.Venue, bool> onVenue, Func<Domain.Pitch, bool> onPitch, Error failure)
    {
        var permission = pitchId is null ? Permissions.VenueEdit : Permissions.PitchesManage;
        if (!(await _access.GetAccessAsync(userId, venueId, cancellationToken)).Can(permission))
        {
            return Result.Failure<IReadOnlyList<string>>(VenueAccessErrors.VenueNotFound);
        }

        IReadOnlyCollection<string> photos;
        if (pitchId is null)
        {
            var venue = await _dbContext.Venues.FirstOrDefaultAsync(v => v.Id == venueId && !v.IsDeleted, cancellationToken);
            if (venue is null)
            {
                return Result.Failure<IReadOnlyList<string>>(VenueAccessErrors.VenueNotFound);
            }

            if (!onVenue(venue))
            {
                return Result.Failure<IReadOnlyList<string>>(failure);
            }

            photos = venue.Photos;
        }
        else
        {
            var pitch = await _dbContext.Pitches.FirstOrDefaultAsync(p => p.Id == pitchId && p.VenueId == venueId && !p.IsDeleted, cancellationToken);
            if (pitch is null)
            {
                return Result.Failure<IReadOnlyList<string>>(VenueAccessErrors.PitchNotFound);
            }

            if (!onPitch(pitch))
            {
                return Result.Failure<IReadOnlyList<string>>(failure);
            }

            photos = pitch.Photos;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success<IReadOnlyList<string>>(photos.ToList());
    }
}
