using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Malabak.Api.Infrastructure.Media;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Bookings.SubmitProof;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Phase 2 (P2) Edge-Case Tests: Payment proof uploads, magic bytes validation,
/// polyglot & path traversal defenses, duplicate receipt detection, and hold freeze (SECURITY.md §12).
/// </summary>
public sealed class PaymentProofAndMediaEdgeCaseTests
{
    private static readonly DateTime _nowUtc = new(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);
    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();
    private readonly Guid _playerId = Guid.NewGuid();

    private static BookingDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BookingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private Booking CreateBooking(BookingDbContext db, DateTime? holdExpiresAtUtc = null)
    {
        var startUtc = _nowUtc.AddHours(2);
        var endUtc = _nowUtc.AddHours(3);
        var booking = Booking.Create(
            Guid.NewGuid(),
            "MLB-TEST-PROOF",
            _venueId,
            _pitchId,
            _playerId,
            startUtc,
            endUtc,
            Money.Create(500m, "EGP").Value,
            Money.Create(100m, "EGP").Value,
            holdExpiresAtUtc ?? _nowUtc.AddMinutes(15),
            null,
            null,
            _nowUtc,
            _playerId.ToString("D")).Value;

        db.Bookings.Add(booking);
        return booking;
    }

    // =========================================================================
    // 1. Magic Bytes & Malicious File Rejection (SECURITY.md §12)
    // =========================================================================

    [Fact]
    public void ImageProcessor_ValidJpegHeader_HasAllowedSignature()
    {
        byte[] validJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
        ImageProcessor.HasAllowedSignature(validJpeg).Should().BeTrue();
    }

    [Fact]
    public void ImageProcessor_ValidPngHeader_HasAllowedSignature()
    {
        byte[] validPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
        ImageProcessor.HasAllowedSignature(validPng).Should().BeTrue();
    }

    [Fact]
    public void ImageProcessor_ValidWebpHeader_HasAllowedSignature()
    {
        byte[] validWebp = [
            (byte)'R', (byte)'I', (byte)'F', (byte)'F',
            0x24, 0x00, 0x00, 0x00,
            (byte)'W', (byte)'E', (byte)'B', (byte)'P'
        ];
        ImageProcessor.HasAllowedSignature(validWebp).Should().BeTrue();
    }

    [Theory]
    [InlineData("<?php echo 'malicious shell'; ?>", "PHP script")]
    [InlineData("<html><script>alert('xss')</script></html>", "HTML payload")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><circle r=\"10\"/></svg>", "SVG XML")]
    [InlineData("%!PS-Adobe-3.0", "PostScript file")]
    [InlineData("MZ\x90\x00\x03\x00\x00\x00", "Windows Executable PE")]
    public void ImageProcessor_DisallowedPayloads_RejectedBySignatureCheck(string payload, string description)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        ImageProcessor.HasAllowedSignature(bytes).Should().BeFalse(description);
        var result = ImageProcessor.Process(bytes);
        result.Success.Should().BeFalse();
        result.Error.Should().Be("unsupported_format");
    }

    [Fact]
    public void ImageProcessor_ZeroByteFile_RejectsWithSizeOutOfRange()
    {
        var result = ImageProcessor.Process([]);
        result.Success.Should().BeFalse();
        result.Error.Should().Be("size_out_of_range");
    }

    [Fact]
    public void ImageProcessor_ExceedsMaxBytes_RejectsWithSizeOutOfRange()
    {
        // 8MB + 1 byte
        var large = new byte[ImageProcessor.MaxBytes + 1];
        var result = ImageProcessor.Process(large);
        result.Success.Should().BeFalse();
        result.Error.Should().Be("size_out_of_range");
    }

    [Fact]
    public void ImageProcessor_ValidPdfReceipt_ShouldBeAccepted()
    {
        // %PDF-1.4 header
        byte[] validPdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<\n>>\nendobj\ntrailer\n<<\n>>\n%%EOF");
        var result = ImageProcessor.Process(validPdf);
        result.Success.Should().BeTrue("PDF payment receipts are explicitly permitted per PRD and security policy");
        result.Variants.Should().ContainSingle();
        result.Variants[0].Name.Should().Be("large");
        result.Variants[0].ContentType.Should().Be("application/pdf");
        result.Variants[0].Bytes.Should().Equal(validPdf, "a PDF is stored as the validated bytes, never re-encoded");
    }

    [Fact]
    public void ImageProcessor_PdfWhereOnlyImagesAreAllowed_IsRejected()
    {
        byte[] validPdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<\n>>\nendobj\ntrailer\n<<\n>>\n%%EOF");
        var result = ImageProcessor.Process(validPdf, allowPdf: false);
        result.Success.Should().BeFalse();
        result.Error.Should().Be("unsupported_format");
    }

    [Theory]
    [InlineData("/JavaScript")]
    [InlineData("/JS")]
    [InlineData("/Launch")]
    [InlineData("/OpenAction")]
    [InlineData("/EmbeddedFile")]
    [InlineData("/RichMedia")]
    public void ImageProcessor_PdfWithActiveContent_IsRejected(string marker)
    {
        var pdf = Encoding.ASCII.GetBytes($"%PDF-1.4\n1 0 obj\n<< {marker} (app.alert(1)) >>\nendobj\ntrailer\n<<\n>>\n%%EOF");
        var result = ImageProcessor.Process(pdf);
        result.Success.Should().BeFalse();
        result.Error.Should().Be("pdf_active_content");
    }

    [Fact]
    public void ImageProcessor_TruncatedPdfWithoutEofMarker_IsRejected()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<\n>>\nendobj\n");
        var result = ImageProcessor.Process(pdf);
        result.Success.Should().BeFalse();
        result.Error.Should().Be("undecodable");
    }

    [Fact]
    public void ImageProcessor_PdfOverSizeLimit_IsRejected()
    {
        var pdf = new byte[ImageProcessor.MaxBytes + 1];
        Encoding.ASCII.GetBytes("%PDF-1.4").CopyTo(pdf, 0);
        var result = ImageProcessor.Process(pdf);
        result.Success.Should().BeFalse();
        result.Error.Should().Be("size_out_of_range");
    }

    // =========================================================================
    // 2. Storage Path Traversal Guard
    // =========================================================================

    [Fact]
    public void MediaStorageKey_IgnoresClientFileName_DoesNotPermitPathTraversal()
    {
        var asset = MediaAsset.Create(Guid.NewGuid(), MediaPurpose.PaymentProof, "token-hash", _nowUtc);
        var method = typeof(MediaService).GetMethod("StorageKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var storageKey = (string)method!.Invoke(null, [asset, "large"])!;

        storageKey.Should().NotContain("..");
        storageKey.Should().StartWith("private/");
        storageKey.Should().EndWith("/large.webp");
    }

    // =========================================================================
    // 3. Duplicate Receipt Hash Detection (TASK-408)
    // =========================================================================

    [Fact]
    public async Task SubmitProof_WhenReceiptHashAlreadyUsedOnAnotherBooking_RejectsWithDuplicateProofAsync()
    {
        await using var db = CreateDbContext();
        var clock = new FakeClock(_nowUtc);

        // Booking 1: submits proof with specific receipt hash
        var booking1 = CreateBooking(db);
        var receiptHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("receipt-12345"))).ToLowerInvariant();

        var proof1 = PaymentProof.Create(
            Guid.NewGuid(),
            booking1.Id,
            Guid.NewGuid(),
            "proofs/user1/proof1/large.webp",
            "receipt.jpg",
            "image/webp",
            2048,
            receiptHash,
            "Transfer #1",
            _nowUtc);

        db.PaymentProofs.Add(proof1);
        await db.SaveChangesAsync();

        // Booking 2: player attempts to submit the same receipt hash
        var booking2 = CreateBooking(db);
        await db.SaveChangesAsync();

        var handler = new SubmitProofHandler(
            db,
            clock,
            new NoOpRealtimeBroadcaster(),
            new StubVenueDirectory(_venueId),
            NullLogger<SubmitProofHandler>.Instance);

        var result = await handler.HandleAsync(new SubmitProofCommand(
            booking2.Id,
            _playerId,
            Guid.NewGuid(),
            "proofs/user1/proof2/large.webp",
            "receipt2.jpg",
            "image/webp",
            2048,
            receiptHash,
            "Reused receipt"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Proof.DuplicateReceipt");
    }

    // =========================================================================
    // 4. Hold Timer Pause on Proof Submission
    // =========================================================================

    [Fact]
    public async Task SubmitProof_PausesHoldTimer_ReaperNeverExpiresProofSubmittedBookingAsync()
    {
        await using var db = CreateDbContext();
        var clock = new FakeClock(_nowUtc);

        var booking = CreateBooking(db, holdExpiresAtUtc: _nowUtc.AddMinutes(15));
        await db.SaveChangesAsync();

        var handler = new SubmitProofHandler(
            db,
            clock,
            new NoOpRealtimeBroadcaster(),
            new StubVenueDirectory(_venueId),
            NullLogger<SubmitProofHandler>.Instance);

        var submitResult = await handler.HandleAsync(new SubmitProofCommand(
            booking.Id,
            _playerId,
            Guid.NewGuid(),
            "proofs/user1/proof/large.webp",
            "receipt.jpg",
            "image/webp",
            1024,
            "hash-unique-12345",
            "My receipt"));

        submitResult.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.ProofSubmitted);

        // Advance clock past hold window (e.g. 60 minutes)
        clock.Advance(TimeSpan.FromMinutes(60));

        // ExpireBookingHoldsJob recurring query only looks for:
        // b.Status == BookingStatus.PendingDeposit && b.HoldExpiresAtUtc <= nowUtc
        var expiredQuery = await db.Bookings
            .Where(b => b.Status == BookingStatus.PendingDeposit && b.HoldExpiresAtUtc <= clock.UtcNow && !b.IsDeleted)
            .ToListAsync();

        expiredQuery.Should().BeEmpty("ProofSubmitted bookings have their expiration paused and must never be reaped");
    }

    // =========================================================================
    // 5. Proof On Another Player's Booking (IDOR Security)
    // =========================================================================

    [Fact]
    public async Task SubmitProof_OnAnotherPlayersBooking_RejectsWithForbiddenAsync()
    {
        await using var db = CreateDbContext();
        var clock = new FakeClock(_nowUtc);

        var booking = CreateBooking(db);
        await db.SaveChangesAsync();

        var attackerPlayerId = Guid.NewGuid();
        var handler = new SubmitProofHandler(
            db,
            clock,
            new NoOpRealtimeBroadcaster(),
            new StubVenueDirectory(_venueId),
            NullLogger<SubmitProofHandler>.Instance);

        var result = await handler.HandleAsync(new SubmitProofCommand(
            booking.Id,
            attackerPlayerId,
            Guid.NewGuid(),
            "proofs/attacker/large.webp",
            "fake.jpg",
            "image/webp",
            1024,
            "hash-attacker",
            "Tampered proof"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.Forbidden");
    }

    // =========================================================================
    // Stubs
    // =========================================================================

    private sealed class StubVenueDirectory(Guid venueId) : IVenueDirectory
    {
        public Task<Guid?> GetVenueIdOfPitchAsync(Guid pitchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(venueId);

        public Task<VenueSummary?> GetVenueSummaryAsync(Guid venueId, CancellationToken cancellationToken = default) =>
            Task.FromResult<VenueSummary?>(null);

        public Task<IReadOnlyList<VenueBlockedPeriod>> GetBlockedPeriodsAsync(Guid venueId, DateTime fromUtc, DateTime toUtc, Guid? pitchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VenueBlockedPeriod>>([]);

        public Task<IReadOnlyDictionary<Guid, string>> GetVenueNamesAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyDictionary<Guid, string>> GetVenueCoverPhotosAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyDictionary<Guid, string>> GetPitchNamesAsync(IReadOnlyCollection<Guid> pitchIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyList<Guid>> GetStaffUserIdsAsync(Guid venueId, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    private sealed class NoOpRealtimeBroadcaster : IBookingRealtimeBroadcaster
    {
        public Task BroadcastSlotStatusChangedAsync(Guid venueId, Guid pitchId, DateTime startUtc, DateTime endUtc, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
