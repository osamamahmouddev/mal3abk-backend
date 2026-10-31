using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Domain;

public sealed class PaymentProof : Entity<Guid>
{
    public Guid BookingId { get; private set; }

    /// <summary>The uploaded image in the media store. Download links are signed from it; null only for proofs saved before it was recorded.</summary>
    public Guid? MediaId { get; private set; }

    public string FileKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public string Sha256Hash { get; private set; } = string.Empty;
    public DateTime UploadedAtUtc { get; private set; }
    public string UploadedByUserId { get; private set; } = string.Empty;
    public bool IsScanClean { get; private set; }
    public DateTime? ScannedAtUtc { get; private set; }
    public string? AntivirusResult { get; private set; }
    public bool? IsApproved { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public string? ReviewedByUserId { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTime? ArchivedAtUtc { get; private set; }

    private PaymentProof()
    {
    }

    public static PaymentProof Create(
        Guid id,
        Guid bookingId,
        Guid? mediaId,
        string fileKey,
        string originalFileName,
        string contentType,
        long fileSizeBytes,
        string sha256Hash,
        string uploadedByUserId,
        DateTime uploadedAtUtc)
    {
        return new PaymentProof
        {
            Id = id,
            BookingId = bookingId,
            MediaId = mediaId,
            FileKey = fileKey,
            OriginalFileName = originalFileName,
            ContentType = contentType,
            FileSizeBytes = fileSizeBytes,
            Sha256Hash = sha256Hash,
            UploadedByUserId = uploadedByUserId,
            UploadedAtUtc = uploadedAtUtc,
            IsScanClean = false // Pending antivirus scan
        };
    }

    public void MarkScanned(bool isClean, string? result, DateTime scannedAtUtc)
    {
        IsScanClean = isClean;
        AntivirusResult = result;
        ScannedAtUtc = scannedAtUtc;
    }

    public void Review(bool isApproved, string? rejectionReason, string reviewedByUserId, DateTime reviewedAtUtc)
    {
        IsApproved = isApproved;
        RejectionReason = rejectionReason;
        ReviewedByUserId = reviewedByUserId;
        ReviewedAtUtc = reviewedAtUtc;
    }

    public void Archive(DateTime archivedAtUtc)
    {
        IsArchived = true;
        ArchivedAtUtc = archivedAtUtc;
    }
}
