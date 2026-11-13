using SkiaSharp;

namespace Malabak.Api.Infrastructure.Media;

public sealed record ImageVariant(string Name, byte[] Bytes, int Width, int Height, string ContentType = ImageProcessor.WebPContentType);

public sealed record ImageProcessingResult(bool Success, string? Error, IReadOnlyList<ImageVariant> Variants)
{
    public static ImageProcessingResult Fail(string error) => new(false, error, []);
}

/// <summary>
/// Validates and re-encodes uploads (SECURITY.md §12): magic-byte check (JPEG/PNG/WebP only), size and pixel limits
/// before decoding (decompression-bomb guard), EXIF orientation applied then all metadata dropped by re-encoding to
/// WebP, and resized variants. Nothing of the original file is stored.
/// <para>
/// Payment receipts may also be PDFs. A PDF cannot be re-encoded safely, so it is stored as the validated original bytes
/// (single "large" variant): the <c>%PDF-</c> magic bytes, the size limit and an <c>%%EOF</c> trailer are checked, and
/// files carrying active content (JavaScript, launch/open actions, embedded files, rich media, forms) are rejected by a
/// scan of the raw bytes. The scan is a coarse filter, not a PDF parser: names inside compressed object streams are not
/// visible to it. The real protection is that the PDF is never rendered by this service: it is stored in the private
/// bucket under a random name, passes the malware scanner, and is only ever served as
/// <c>Content-Disposition: attachment</c> with <c>nosniff</c>, a sandboxing CSP and a short-lived signed URL.
/// </para>
/// </summary>
public static class ImageProcessor
{
    public const long MaxBytes = 8 * 1024 * 1024;
    public const int MaxDimension = 8000;
    public const long MaxPixels = 40_000_000;
    public const string WebPContentType = "image/webp";
    public const string PdfContentType = "application/pdf";
    private const int _quality = 82;
    private const int _pdfTrailerWindow = 1024;

    /// <summary>Markers of PDF features that can run code or reach outside the file (matched case-sensitively as PDF names).</summary>
    private static readonly byte[][] _pdfActiveContentMarkers =
        new[] { "/JavaScript", "/JS", "/Launch", "/OpenAction", "/EmbeddedFile", "/RichMedia", "/XFA", "/SubmitForm", "/GoToR", "/GoToE" }
            .Select(System.Text.Encoding.ASCII.GetBytes).ToArray();

    public static readonly IReadOnlyList<(string Name, int MaxSide)> Variants = [("large", 1920), ("medium", 960), ("thumb", 320)];

    public static bool HasAllowedSignature(ReadOnlySpan<byte> header) =>
        header.Length >= 12 && (
            (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) ||                                   // JPEG
            header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) ||        // PNG
            (header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8)));              // WebP

    public static bool IsPdf(ReadOnlySpan<byte> header) => header.StartsWith("%PDF-"u8);

    /// <param name="allowPdf">True only for purposes that accept documents (payment proofs); galleries and avatars are images only.</param>
    public static ImageProcessingResult Process(byte[] data, bool allowPdf = true)
    {
        if (data.Length == 0 || data.Length > MaxBytes)
        {
            return ImageProcessingResult.Fail("size_out_of_range");
        }

        if (IsPdf(data))
        {
            return allowPdf ? ProcessPdf(data) : ImageProcessingResult.Fail("unsupported_format");
        }

        if (!HasAllowedSignature(data))
        {
            return ImageProcessingResult.Fail("unsupported_format");
        }

        using var codec = SKCodec.Create(new SKMemoryStream(data));
        if (codec is null)
        {
            return ImageProcessingResult.Fail("undecodable");
        }

        // Check declared dimensions before allocating pixels.
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > MaxDimension || info.Height > MaxDimension || (long)info.Width * info.Height > MaxPixels)
        {
            return ImageProcessingResult.Fail("dimensions_out_of_range");
        }

        using var decoded = SKBitmap.Decode(codec, info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Premul));
        if (decoded is null)
        {
            return ImageProcessingResult.Fail("undecodable");
        }

        using var upright = ApplyOrientation(decoded, codec.EncodedOrigin);

        var variants = new List<ImageVariant>();
        foreach (var (name, maxSide) in Variants)
        {
            var scale = Math.Min(1.0, (double)maxSide / Math.Max(upright.Width, upright.Height));
            var width = Math.Max(1, (int)Math.Round(upright.Width * scale));
            var height = Math.Max(1, (int)Math.Round(upright.Height * scale));

            using var resized = upright.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKCubicResampler.Mitchell));
            if (resized is null)
            {
                return ImageProcessingResult.Fail("resize_failed");
            }

            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, _quality);
            variants.Add(new ImageVariant(name, encoded.ToArray(), width, height));
        }

        return new ImageProcessingResult(true, null, variants);
    }

    private static ImageProcessingResult ProcessPdf(byte[] data)
    {
        // A real PDF ends with %%EOF (trailing whitespace allowed); anything else is truncated or not a PDF.
        var tail = data.AsSpan(Math.Max(0, data.Length - _pdfTrailerWindow));
        if (!tail.IsEmpty && tail.IndexOf("%%EOF"u8) < 0)
        {
            return ImageProcessingResult.Fail("undecodable");
        }

        foreach (var marker in _pdfActiveContentMarkers)
        {
            if (data.AsSpan().IndexOf(marker) >= 0)
            {
                return ImageProcessingResult.Fail("pdf_active_content");
            }
        }

        return new ImageProcessingResult(true, null, [new ImageVariant("large", data, 0, 0, PdfContentType)]);
    }

    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        var rotated = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(rotated ? source.Height : source.Width, rotated ? source.Width : source.Height, source.ColorType, source.AlphaType);

        using var canvas = new SKCanvas(result);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1, result.Width / 2f, 0); break;
            case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, result.Width / 2f, result.Height / 2f); break;
            case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1, 0, result.Height / 2f); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(result.Width, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, result.Height); canvas.RotateDegrees(270); break;
            case SKEncodedOrigin.LeftTop: canvas.Translate(result.Width, 0); canvas.RotateDegrees(90); canvas.Scale(1, -1, 0, source.Height / 2f); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(0, result.Height); canvas.RotateDegrees(270); canvas.Scale(1, -1, 0, source.Height / 2f); break;
            default: break;
        }

        canvas.DrawBitmap(source, 0, 0);
        return result;
    }
}
