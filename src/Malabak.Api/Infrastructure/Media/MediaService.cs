using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Api.Infrastructure.Media;

public sealed record UploadTicket(Guid MediaId, string UploadUrl, DateTime ExpiresAtUtc, long MaxBytes);

public sealed record MediaUploadResult(bool Success, string? Error, Guid MediaId, IReadOnlyDictionary<string, string> Urls);

public sealed record MediaContent(byte[] Bytes, bool IsPublic, string ContentType = ImageProcessor.WebPContentType)
{
    /// <summary>Documents are never shown inline: they are offered as a download.</summary>
    public bool IsDocument => ContentType == ImageProcessor.PdfContentType;
}

/// <summary>
/// Upload flow: (1) POST creates a 5-minute, single-use upload URL bound to the user and purpose; (2) the client PUTs
/// the bytes there; (3) we validate, re-encode, store variants and mark the asset ready. Public variants are served
/// with immutable CDN caching; private ones only through short-lived HMAC-signed URLs.
/// </summary>
public sealed class MediaService
{
    public const string BasePath = "/api/v1/media";
    public static readonly TimeSpan PrivateUrlLifetime = TimeSpan.FromMinutes(5);

    private readonly MediaDbContext _dbContext;
    private readonly IObjectStorage _storage;
    private readonly IMalwareScanner _scanner;
    private readonly IClock _clock;
    private readonly ILogger<MediaService> _logger;
    private static readonly byte[] _developmentSigningKey = RandomNumberGenerator.GetBytes(32);

    private readonly byte[] _signingKey;

    public MediaService(
        MediaDbContext dbContext,
        IObjectStorage storage,
        IMalwareScanner scanner,
        IClock clock,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<MediaService> logger)
    {
        _dbContext = dbContext;
        _storage = storage;
        _scanner = scanner;
        _clock = clock;
        _logger = logger;

        var key = configuration["Media:SigningKey"];
        if (!string.IsNullOrWhiteSpace(key))
        {
            _signingKey = Encoding.UTF8.GetBytes(key);
        }
        else if (environment.IsDevelopment())
        {
            // One key per process, shared by every request: the service is scoped, so a key made in the constructor would differ
            // between the request that signs a link and the request that opens it, and every private image would 404.
            _signingKey = _developmentSigningKey;
        }
        else
        {
            throw new InvalidOperationException("Required secret 'Media:SigningKey' is not configured.");
        }
    }

    public async Task<UploadTicket> CreateUploadAsync(Guid userId, MediaPurpose purpose, CancellationToken cancellationToken = default)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var asset = MediaAsset.Create(userId, purpose, Hash(raw), _clock.UtcNow);
        _dbContext.Assets.Add(asset);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new UploadTicket(asset.Id, $"{BasePath}/uploads/{asset.Id:D}?token={raw}", asset.UploadExpiresAtUtc, ImageProcessor.MaxBytes);
    }

    public async Task<MediaUploadResult> UploadAsync(Guid mediaId, Guid userId, string token, byte[] content, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var asset = await _dbContext.Assets.FirstOrDefaultAsync(a => a.Id == mediaId && a.OwnerUserId == userId, cancellationToken);
        if (asset is null || !asset.CanUpload(nowUtc) || string.IsNullOrEmpty(token)
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(asset.UploadTokenHash), Encoding.ASCII.GetBytes(Hash(token))))
        {
            return Fail(mediaId, "invalid_or_expired_upload");
        }

        var processed = ImageProcessor.Process(content, allowPdf: asset.Purpose == MediaPurpose.PaymentProof);
        if (!processed.Success || !await _scanner.IsCleanAsync(content, cancellationToken))
        {
            asset.Reject(processed.Error ?? "malware_detected", nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Upload {MediaId} rejected: {Reason}", mediaId, asset.RejectionReason);
            return Fail(mediaId, asset.RejectionReason!);
        }

        foreach (var variant in processed.Variants)
        {
            await _storage.PutAsync(KeyFor(asset, variant.Name, variant.ContentType), variant.Bytes, variant.ContentType, cancellationToken);
        }

        var large = processed.Variants[0];
        asset.MarkReady(large.Width, large.Height, processed.Variants.Sum(v => (long)v.Bytes.Length), Convert.ToHexString(SHA256.HashData(large.Bytes)), nowUtc, large.ContentType);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new MediaUploadResult(true, null, asset.Id, UrlsFor(asset));
    }

    /// <summary>A ready asset owned by the user with the expected purpose (used before attaching it to a gallery/profile).</summary>
    public Task<MediaAsset?> FindOwnedReadyAsync(Guid mediaId, Guid userId, MediaPurpose purpose, CancellationToken cancellationToken = default) =>
        _dbContext.Assets.AsNoTracking().FirstOrDefaultAsync(
            a => a.Id == mediaId && a.OwnerUserId == userId && a.Purpose == purpose && a.Status == MediaStatus.Ready, cancellationToken);

    /// <summary>Public assets for anyone; private ones only with a valid, unexpired signature.</summary>
    public async Task<MediaContent?> GetVariantAsync(Guid mediaId, string variant, string? expires, string? signature, CancellationToken cancellationToken = default)
    {
        var asset = await _dbContext.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == mediaId && a.Status == MediaStatus.Ready, cancellationToken);
        if (asset is null || !VariantNames(asset).Contains(variant) || (asset.Visibility == MediaVisibility.Private && !IsValidSignature(mediaId, variant, expires, signature)))
        {
            return null;
        }

        var bytes = await _storage.GetAsync(StorageKey(asset, variant), cancellationToken);
        return bytes is null ? null : new MediaContent(bytes, asset.Visibility == MediaVisibility.Public, asset.ContentType);
    }

    public IReadOnlyDictionary<string, string> UrlsFor(MediaAsset asset) =>
        VariantNames(asset).ToDictionary(name => name, name => asset.Visibility == MediaVisibility.Public
            ? $"{BasePath}/{asset.Id:D}/{name}"
            : SignedUrl(asset.Id, name));

    /// <summary>An image has its three resized variants; a PDF only has the stored original under "large".</summary>
    private static IReadOnlyList<string> VariantNames(MediaAsset asset) =>
        asset.IsPdf ? ["large"] : ImageProcessor.Variants.Select(v => v.Name).ToList();

    public static string PublicUrl(Guid mediaId, string variant = "large") => $"{BasePath}/{mediaId:D}/{variant}";

    internal string SignedUrl(Guid mediaId, string variant)
    {
        var expires = new DateTimeOffset(_clock.UtcNow + PrivateUrlLifetime).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return $"{BasePath}/{mediaId:D}/{variant}?exp={expires}&sig={Sign(mediaId, variant, expires)}";
    }

    internal bool IsValidSignature(Guid mediaId, string variant, string? expires, string? signature)
    {
        if (!long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out var unix) || string.IsNullOrEmpty(signature)
            || DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime < _clock.UtcNow)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(mediaId, variant, expires!)), Encoding.ASCII.GetBytes(signature));
    }

    private string Sign(Guid mediaId, string variant, string expires) =>
        Convert.ToHexString(HMACSHA256.HashData(_signingKey, Encoding.UTF8.GetBytes($"{mediaId:N}:{variant}:{expires}"))).ToLowerInvariant();

    /// <summary>Deletes every stored variant and the asset record (account erasure, ADR-0007).</summary>
    public async Task DeleteAsync(MediaAsset asset, CancellationToken cancellationToken = default)
    {
        foreach (var name in VariantNames(asset))
        {
            await _storage.DeleteAsync(StorageKey(asset, name), cancellationToken);
        }

        _dbContext.Assets.Remove(asset);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The name is built from server-side values only (visibility, asset id, variant, format); the client's file name is never used.</summary>
    internal static string StorageKey(MediaAsset asset, string variant) => KeyFor(asset, variant, asset.ContentType);

    private static string KeyFor(MediaAsset asset, string variant, string contentType) =>
        $"{asset.Visibility.ToString().ToLowerInvariant()}/{asset.Id:N}/{variant}.{(contentType == ImageProcessor.PdfContentType ? "pdf" : "webp")}";

    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private static MediaUploadResult Fail(Guid mediaId, string error) => new(false, error, mediaId, new Dictionary<string, string>());
}
