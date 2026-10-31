namespace Malabak.Modules.Booking.Application.Abstractions;

/// <summary>Removes the stored image of a payment proof. Implemented by the host on top of the media store.</summary>
public interface IProofFileStore
{
    /// <summary>Deletes every stored variant and the media record. Succeeds when the image is already gone.</summary>
    Task DeleteAsync(Guid mediaId, CancellationToken cancellationToken = default);
}
