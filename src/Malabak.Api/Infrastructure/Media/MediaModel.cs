using Microsoft.EntityFrameworkCore;

namespace Malabak.Api.Infrastructure.Media;

public enum MediaPurpose
{
    VenuePhoto = 0,
    PitchPhoto = 1,
    Avatar = 2,
    PaymentProof = 3
}

public enum MediaVisibility
{
    /// <summary>Served to anyone with long-lived CDN caching (venue galleries, avatars).</summary>
    Public = 0,

    /// <summary>Served only through short-lived signed URLs (payment proofs).</summary>
    Private = 1
}

public enum MediaStatus
{
    AwaitingUpload = 0,
    Ready = 1,
    Rejected = 2
}

/// <summary>An uploaded image. Only re-encoded variants are ever stored; the original bytes are discarded.</summary>
public sealed class MediaAsset
{
    public static readonly TimeSpan UploadWindow = TimeSpan.FromMinutes(5);

    private MediaAsset()
    {
    }

    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public MediaPurpose Purpose { get; private set; }
    public MediaVisibility Visibility { get; private set; }
    public MediaStatus Status { get; private set; }
    public string UploadTokenHash { get; private set; } = string.Empty;
    public DateTime UploadExpiresAtUtc { get; private set; }
    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public long? SizeBytes { get; private set; }

    /// <summary>
    /// SHA-256 of the processed, canonical image (hex). Identical pictures give identical hashes, whatever file format they
    /// were uploaded in, which is what lets a payment receipt reused on another booking be recognised.
    /// </summary>
    public string? ContentSha256 { get; private set; }

    /// <summary>"image/webp" for re-encoded images; "application/pdf" for a stored payment-receipt PDF.</summary>
    public string ContentType { get; private set; } = ImageProcessor.WebPContentType;

    public bool IsPdf => ContentType == ImageProcessor.PdfContentType;

    public string? RejectionReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public static MediaAsset Create(Guid ownerUserId, MediaPurpose purpose, string uploadTokenHash, DateTime nowUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        OwnerUserId = ownerUserId,
        Purpose = purpose,
        Visibility = purpose == MediaPurpose.PaymentProof ? MediaVisibility.Private : MediaVisibility.Public,
        Status = MediaStatus.AwaitingUpload,
        UploadTokenHash = uploadTokenHash,
        UploadExpiresAtUtc = nowUtc + UploadWindow,
        CreatedAtUtc = nowUtc
    };

    public bool CanUpload(DateTime nowUtc) => Status == MediaStatus.AwaitingUpload && UploadExpiresAtUtc > nowUtc;

    public void MarkReady(int width, int height, long sizeBytes, string contentSha256, DateTime nowUtc, string contentType = ImageProcessor.WebPContentType)
    {
        ContentType = contentType;
        Status = MediaStatus.Ready;
        Width = width;
        Height = height;
        SizeBytes = sizeBytes;
        ContentSha256 = contentSha256;
        CompletedAtUtc = nowUtc;
        UploadTokenHash = string.Empty; // single use
    }

    public void Reject(string reason, DateTime nowUtc)
    {
        Status = MediaStatus.Rejected;
        RejectionReason = reason;
        CompletedAtUtc = nowUtc;
        UploadTokenHash = string.Empty;
    }
}

public sealed class MediaDbContext : DbContext
{
    public const string Schema = "media";

    public DbSet<MediaAsset> Assets => Set<MediaAsset>();

    public MediaDbContext(DbContextOptions<MediaDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<MediaAsset>(b =>
        {
            b.ToTable("assets");
            b.HasKey(a => a.Id);
            b.Property(a => a.Purpose).HasConversion<string>().HasMaxLength(20);
            b.Property(a => a.Visibility).HasConversion<string>().HasMaxLength(10);
            b.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(a => a.UploadTokenHash).HasMaxLength(64);
            b.Property(a => a.ContentType).HasMaxLength(30).HasDefaultValue(ImageProcessor.WebPContentType);
            b.Property(a => a.ContentSha256).HasMaxLength(64);
            b.Property(a => a.RejectionReason).HasMaxLength(60);
            b.HasIndex(a => new { a.OwnerUserId, a.CreatedAtUtc });
        });
    }
}
