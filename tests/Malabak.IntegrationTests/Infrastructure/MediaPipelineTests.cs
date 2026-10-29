using System.Text;
using FluentAssertions;
using Malabak.Api.Infrastructure.Media;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class MediaPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "malabak-media-tests", Guid.NewGuid().ToString("N"));
    private readonly MutableClock _clock = new();
    private readonly Guid _userId = Guid.NewGuid();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static byte[] Image(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.ForestGreen);
        }

        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(format, 90).ToArray();
    }

    [Fact]
    public void Process_ValidJpeg_ProducesWebpVariants_ScaledDown()
    {
        var result = ImageProcessor.Process(Image(3000, 2000));

        result.Success.Should().BeTrue();
        result.Variants.Select(v => v.Name).Should().Equal("large", "medium", "thumb");
        result.Variants[0].Width.Should().Be(1920);
        result.Variants[2].Width.Should().Be(320);
        result.Variants.Should().OnlyContain(v => ImageProcessor.HasAllowedSignature(v.Bytes) && Encoding.ASCII.GetString(v.Bytes, 8, 4) == "WEBP");
    }

    [Fact]
    public void Process_DoesNotUpscaleSmallImages()
    {
        var result = ImageProcessor.Process(Image(200, 100, SKEncodedImageFormat.Png));

        result.Variants.Should().OnlyContain(v => v.Width == 200 && v.Height == 100);
    }

    [Fact]
    public void Process_RejectsNonImagesAndDisguisedFiles()
    {
        ImageProcessor.Process(Encoding.UTF8.GetBytes("<?php system($_GET['c']); ?> padding padding")).Error.Should().Be("unsupported_format");

        // Valid PNG signature followed by junk: passes the magic check but must fail decoding.
        byte[] fakePng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Encoding.ASCII.GetBytes("not really an image at all")];
        ImageProcessor.Process(fakePng).Success.Should().BeFalse();

        ImageProcessor.Process([]).Error.Should().Be("size_out_of_range");
    }

    [Fact]
    public void Process_RejectsOversizedDimensions_BeforeDecoding()
    {
        ImageProcessor.Process(Image(9000, 10, SKEncodedImageFormat.Png)).Error.Should().Be("dimensions_out_of_range");
    }

    [Fact]
    public void Process_OutputCarriesNoExifMetadata()
    {
        var result = ImageProcessor.Process(Image(800, 600));

        result.Variants.Should().OnlyContain(v => !Encoding.ASCII.GetString(v.Bytes).Contains("Exif", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UploadFlow_Ticket_Upload_Ready_PublicServe_AndTokenSingleUseAsync()
    {
        await using var db = NewDb();
        var service = Service(db);

        var ticket = await service.CreateUploadAsync(_userId, MediaPurpose.VenuePhoto);
        var token = TokenFrom(ticket.UploadUrl);
        var result = await service.UploadAsync(ticket.MediaId, _userId, token, Image(1200, 800));

        result.Success.Should().BeTrue();
        result.Urls["thumb"].Should().Be($"/api/v1/media/{ticket.MediaId:D}/thumb");
        (await service.GetVariantAsync(ticket.MediaId, "thumb", null, null))!.IsPublic.Should().BeTrue();
        (await service.UploadAsync(ticket.MediaId, _userId, token, Image(10, 10))).Error.Should().Be("invalid_or_expired_upload");
        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Should().OnlyContain(f => f.EndsWith(".webp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Upload_ByAnotherUser_OrAfterExpiry_IsRejectedAsync()
    {
        await using var db = NewDb();
        var service = Service(db);
        var ticket = await service.CreateUploadAsync(_userId, MediaPurpose.Avatar);
        var token = TokenFrom(ticket.UploadUrl);

        (await service.UploadAsync(ticket.MediaId, Guid.NewGuid(), token, Image(100, 100))).Success.Should().BeFalse();

        _clock.UtcNow = _clock.UtcNow.AddMinutes(6);
        (await service.UploadAsync(ticket.MediaId, _userId, token, Image(100, 100))).Success.Should().BeFalse();
    }

    [Fact]
    public async Task PrivateMedia_RequiresValidUnexpiredSignatureAsync()
    {
        await using var db = NewDb();
        var service = Service(db);
        var ticket = await service.CreateUploadAsync(_userId, MediaPurpose.PaymentProof);
        var result = await service.UploadAsync(ticket.MediaId, _userId, TokenFrom(ticket.UploadUrl), Image(600, 900));
        var signed = new Uri("http://x" + result.Urls["large"]);
        var query = System.Web.HttpUtility.ParseQueryString(signed.Query);

        (await service.GetVariantAsync(ticket.MediaId, "large", null, null)).Should().BeNull("private media needs a signature");
        (await service.GetVariantAsync(ticket.MediaId, "large", query["exp"], "tampered")).Should().BeNull();
        (await service.GetVariantAsync(ticket.MediaId, "large", query["exp"], query["sig"]))!.IsPublic.Should().BeFalse();

        _clock.UtcNow = _clock.UtcNow.AddMinutes(6);
        (await service.GetVariantAsync(ticket.MediaId, "large", query["exp"], query["sig"])).Should().BeNull("signed URLs expire");
    }

    [Fact]
    public async Task FindOwnedReady_OnlyReturnsCallersReadyAssetOfThatPurposeAsync()
    {
        await using var db = NewDb();
        var service = Service(db);
        var ticket = await service.CreateUploadAsync(_userId, MediaPurpose.VenuePhoto);

        (await service.FindOwnedReadyAsync(ticket.MediaId, _userId, MediaPurpose.VenuePhoto)).Should().BeNull("not uploaded yet");
        await service.UploadAsync(ticket.MediaId, _userId, TokenFrom(ticket.UploadUrl), Image(300, 300));

        (await service.FindOwnedReadyAsync(ticket.MediaId, _userId, MediaPurpose.VenuePhoto)).Should().NotBeNull();
        (await service.FindOwnedReadyAsync(ticket.MediaId, Guid.NewGuid(), MediaPurpose.VenuePhoto)).Should().BeNull();
        (await service.FindOwnedReadyAsync(ticket.MediaId, _userId, MediaPurpose.Avatar)).Should().BeNull();
    }

    private MediaService Service(MediaDbContext db)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Media:LocalRoot"] = _root,
            ["Media:SigningKey"] = "media-signing-key-for-tests-0123456789"
        }).Build();
        var env = new TestEnv();
        return new MediaService(db, new LocalObjectStorage(config, env), new NoOpMalwareScanner(), _clock, config, env, NullLogger<MediaService>.Instance);
    }

    private static string TokenFrom(string uploadUrl) => uploadUrl[(uploadUrl.IndexOf("token=", StringComparison.Ordinal) + 6)..];

    private static MediaDbContext NewDb() =>
        new(new DbContextOptionsBuilder<MediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class TestEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Malabak.Api";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class MutableClock : IClock
    {
        public DateTime UtcNow { get; set; } = DateTime.UtcNow;
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
