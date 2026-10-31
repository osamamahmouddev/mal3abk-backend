using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Application.Bookings.SubmitProof;

public sealed record SubmitProofCommand(
    Guid BookingId,
    Guid PlayerId,
    Guid MediaId,
    string FileKey,
    string OriginalFileName,
    string ContentType,
    long FileSizeBytes,
    string Sha256Hash,
    string? Notes = null
) : ICommand<Result>;
