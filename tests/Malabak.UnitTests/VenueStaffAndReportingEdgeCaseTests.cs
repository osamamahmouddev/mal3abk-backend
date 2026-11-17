using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Malabak.Modules.Booking.Application.Customers;
using Malabak.Modules.Booking.Application.Reports;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Gallery;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using BookingEntity = Malabak.Modules.Booking.Domain.Booking;

namespace Malabak.UnitTests;

/// <summary>
/// Edge-case test suite for Venue Staff Management (48h expiry, roles, least privilege),
/// Customer Notes Isolation & Encryption, CSV Formula Injection Sanitization, and Gallery Limits (P3).
/// </summary>
public sealed class VenueStaffAndReportingEdgeCaseTests
{
    private static readonly DateTime _now = new(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _otherVenueId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _staffUserId = Guid.NewGuid();
    private readonly Guid _otherStaffUserId = Guid.NewGuid();
    private readonly FixedClock _clock = new();

    private static VenuesDbContext CreateVenuesDb()
    {
        var options = new DbContextOptionsBuilder<VenuesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new VenuesDbContext(options);
    }

    private static BookingDbContext CreateBookingDb()
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new BookingDbContext(options);
    }

    [Fact]
    public void StaffInvitation_RequiresExactlyOneContactMethod()
    {
        // Neither email nor phone -> throws
        var actNeither = () => VenueStaffInvitation.Create(_venueId, null, null, VenueStaffRole.Receptionist, "hash123", _ownerId, _now);
        actNeither.Should().Throw<ArgumentException>().WithMessage("*Invite by exactly one of email or phone*");

        // Both email and phone -> throws
        var actBoth = () => VenueStaffInvitation.Create(_venueId, "staff@test.com", "+201012345678", VenueStaffRole.Receptionist, "hash123", _ownerId, _now);
        actBoth.Should().Throw<ArgumentException>().WithMessage("*Invite by exactly one of email or phone*");

        // Exactly one -> succeeds
        var invEmail = VenueStaffInvitation.Create(_venueId, "staff@test.com", null, VenueStaffRole.Receptionist, "hash123", _ownerId, _now);
        invEmail.Email.Should().Be("staff@test.com");
        invEmail.PhoneNumber.Should().BeNull();

        var invPhone = VenueStaffInvitation.Create(_venueId, null, "+201012345678", VenueStaffRole.Receptionist, "hash456", _ownerId, _now);
        invPhone.Email.Should().BeNull();
        invPhone.PhoneNumber.Should().Be("+201012345678");
    }

    [Fact]
    public void StaffInvitation_48HourExpiry_AndResendReset()
    {
        var inv = VenueStaffInvitation.Create(_venueId, "staff@test.com", null, VenueStaffRole.Manager, "token_hash_1", _ownerId, _now);

        // Created at _now; Lifetime is 48 hours
        inv.ExpiresAtUtc.Should().Be(_now + TimeSpan.FromHours(48));

        // At 47 hours: still usable
        inv.IsUsable(_now + TimeSpan.FromHours(47)).Should().BeTrue();

        // At 48 hours + 1 second: expired!
        inv.IsUsable(_now + TimeSpan.FromHours(48).Add(TimeSpan.FromSeconds(1))).Should().BeFalse();

        // Resend at 40 hours -> issues new token and gives a fresh 48 hours from reissue time
        var reissueTime = _now + TimeSpan.FromHours(40);
        inv.Reissue("new_token_hash_2", reissueTime);

        inv.TokenHash.Should().Be("new_token_hash_2");
        inv.ExpiresAtUtc.Should().Be(reissueTime + TimeSpan.FromHours(48));
        inv.IsUsable(reissueTime + TimeSpan.FromHours(47)).Should().BeTrue();
    }

    [Fact]
    public void StaffRoles_EnforceStrictLeastPrivilegePermissions()
    {
        var managerPerms = VenueStaffRolePermissions.For(VenueStaffRole.Manager);
        var receptionistPerms = VenueStaffRolePermissions.For(VenueStaffRole.Receptionist);
        var accountantPerms = VenueStaffRolePermissions.For(VenueStaffRole.Accountant);

        // Receptionist can view bookings, but CANNOT review payment proofs or view financial reports
        receptionistPerms.Should().Contain(Permissions.BookingsView);
        receptionistPerms.Should().NotContain(Permissions.PaymentProofsReview);
        receptionistPerms.Should().NotContain(Permissions.ReportsView);
        receptionistPerms.Should().NotContain(Permissions.PitchesManage);

        // Accountant can review payment proofs and view reports, but CANNOT manage pitches or blocked slots
        accountantPerms.Should().Contain(Permissions.PaymentProofsReview);
        accountantPerms.Should().Contain(Permissions.ReportsView);
        accountantPerms.Should().NotContain(Permissions.PitchesManage);
        accountantPerms.Should().NotContain(Permissions.BlockedSlotsManage);

        // Manager has operational permissions but owner preserves exclusive rights (staff management stays with owner)
        managerPerms.Should().Contain(Permissions.PitchesManage);
        managerPerms.Should().Contain(Permissions.SchedulesManage);
        managerPerms.Should().Contain(Permissions.PaymentProofsReview);
    }

    [Fact]
    public async Task CustomerNotes_EncryptedAtRest_AndIsolatedBetweenVenuesAsync()
    {
        await using var db = CreateBookingDb();
        var playerId = Guid.NewGuid();
        var booking = BookingEntity.Create(
            Guid.NewGuid(), "MLB-CUST-1", _venueId, Guid.NewGuid(), playerId,
            _now.AddDays(-1), _now.AddDays(-1).AddHours(1),
            Money.From(400), Money.From(100), _now.AddDays(30), null, null, _now.AddDays(-2), "p").Value;
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var cipher = new FakeFieldCipher();
        var blindIndex = new FakeBlindIndex();
        var service = new VenueCustomerService(
            db,
            new FakeUsers(),
            blindIndex,
            cipher,
            _clock);

        var playerKey = $"player:{playerId:N}";

        // Staff at venue 1 adds a sensitive customer note
        var addedNote = (await service.AddNoteAsync(_venueId, playerKey, _staffUserId, "VIP regular: prefers pitch 1, allergic to peanuts.")).Value;

        // Verify note text is encrypted in database
        var rawStored = await db.CustomerNotes.SingleAsync();
        Encoding.UTF8.GetString(rawStored.TextCiphertext).Should().NotContain("allergic", "Sensitive note text must be encrypted at rest");

        // Staff at other venue attempting to delete note of venue 1 -> NoteNotFound
        var deleteFromOther = await service.DeleteNoteAsync(_otherVenueId, playerKey, addedNote.Id, _otherStaffUserId, canDeleteAnyNote: true);
        deleteFromOther.IsFailure.Should().BeTrue();
        deleteFromOther.Error.Code.Should().Be("Customer.NoteNotFound");

        // Staff without canDeleteAnyNote permission cannot delete note authored by another staff
        var unauthorizedDelete = await service.DeleteNoteAsync(_venueId, playerKey, addedNote.Id, _otherStaffUserId, canDeleteAnyNote: false);
        unauthorizedDelete.IsFailure.Should().BeTrue();
        unauthorizedDelete.Error.Code.Should().Be("Customer.CannotDeleteNote");

        // Author can delete their own note
        var authorDelete = await service.DeleteNoteAsync(_venueId, playerKey, addedNote.Id, _staffUserId, canDeleteAnyNote: false);
        authorDelete.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("=SUM(A1:A10)", "\"'=SUM(A1:A10)\"")]
    [InlineData("+123456789", "\"'+123456789\"")]
    [InlineData("-100.50", "\"'-100.50\"")]
    [InlineData("@command", "\"'@command\"")]
    [InlineData("\tleading-tab", "\"'\\tleading-tab\"")]
    [InlineData("=cmd|' /C calc'!A0", "\"'=cmd|' /C calc'!A0\"")]
    [InlineData("Normal Player Name", "Normal Player Name")]
    [InlineData("Name with, comma", "\"Name with, comma\"")]
    [InlineData("Name with \"quotes\"", "\"Name with \"\"quotes\"\"\"")]
    public void CsvExport_CellSanitization_NeutralizesFormulaInjection(string input, string expected)
    {
        // Neutralizes formula injection by prepending apostrophe when cell starts with =+-@\t\r
        var sanitized = VenueReportService.Cell(input.Replace("\\t", "\t"));
        sanitized.Should().Be(expected.Replace("\\t", "\t"));
    }

    [Fact]
    public async Task VenueReport_DateRangeValidation_EnforcesMaximum366DaysAsync()
    {
        await using var db = CreateBookingDb();
        var from = new DateOnly(2026, 1, 1);
        var toValid = new DateOnly(2026, 6, 1);
        var toTooFar = new DateOnly(2027, 2, 1); // > 366 days

        var service = new VenueReportService(db, new FakeCapacityReader(), new FakeUsers(), _clock);

        // Date range in reverse order (to < from)
        var invalidReversed = await service.GetAsync(_venueId, toValid, from, null, ReportGrouping.Day);
        invalidReversed.IsFailure.Should().BeTrue();
        invalidReversed.Error.Code.Should().Be("Report.InvalidRange");

        // Date range > 366 days
        var invalidSpan = await service.GetAsync(_venueId, from, toTooFar, null, ReportGrouping.Day);
        invalidSpan.IsFailure.Should().BeTrue();
        invalidSpan.Error.Code.Should().Be("Report.InvalidRange");
    }

    [Fact]
    public async Task GalleryPhotos_EnforcesMaximum20PhotosPerVenueAsync()
    {
        await using var db = CreateVenuesDb();
        var venue = Venue.Create(_venueId, "Club", "club", null, _ownerId, "Cairo", "Cairo", "Maadi", null, null, "+201000000000", null, null, _now);
        db.Venues.Add(venue);
        await db.SaveChangesAsync();

        var access = new FakeVenueAccess(VenueAccess.Owner(_venueId));
        var handler = new GalleryHandlers(db, access, _clock);

        // Add 20 unique photos -> all succeed
        for (var i = 1; i <= 20; i++)
        {
            var add = await handler.HandleAsync(new AddGalleryPhotoCommand(_venueId, null, _ownerId, $"https://cdn.mal3abk.com/venues/{_venueId}/photo_{i}.webp"));
            add.IsSuccess.Should().BeTrue();
            add.Value.Should().HaveCount(i);
        }

        // Add 21st photo -> fails with Gallery.Full
        var photo21 = await handler.HandleAsync(new AddGalleryPhotoCommand(_venueId, null, _ownerId, $"https://cdn.mal3abk.com/venues/{_venueId}/photo_21.webp"));
        photo21.IsFailure.Should().BeTrue();
        photo21.Error.Code.Should().Be(GalleryErrors.Full.Code);

        // Add duplicate photo -> fails with Gallery.Full
        var duplicatePhoto = await handler.HandleAsync(new AddGalleryPhotoCommand(_venueId, null, _ownerId, $"https://cdn.mal3abk.com/venues/{_venueId}/photo_1.webp"));
        duplicatePhoto.IsFailure.Should().BeTrue();
        duplicatePhoto.Error.Code.Should().Be(GalleryErrors.Full.Code);
    }

    [Fact]
    public async Task GalleryPhotoRemoval_UsesMediaIdWhenStoredVariantDiffersAsync()
    {
        await using var db = CreateVenuesDb();
        var venue = Venue.Create(_venueId, "Club", "club", null, _ownerId, "Cairo", "Cairo", "Maadi", null, null, "+201000000000", null, null, _now);
        db.Venues.Add(venue);
        await db.SaveChangesAsync();

        var mediaId = Guid.NewGuid();
        var storedPhoto = $"/api/v1/media/{mediaId:D}/medium";
        var handler = new GalleryHandlers(db, new FakeVenueAccess(VenueAccess.Owner(_venueId)), _clock);
        (await handler.HandleAsync(new AddGalleryPhotoCommand(_venueId, null, _ownerId, storedPhoto))).IsSuccess.Should().BeTrue();

        var result = await handler.HandleAsync(new RemoveGalleryPhotoCommand(
            _venueId, null, _ownerId, $"/api/v1/media/{mediaId:D}/large", mediaId));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    private sealed class FakeVenueAccess(VenueAccess access) : IVenueAccessService
    {
        public Task<VenueAccess> GetAccessAsync(Guid userId, Guid venueId, CancellationToken cancellationToken = default) => Task.FromResult(access);
        public Task<IReadOnlyList<VenueAccess>> GetAccessibleVenuesAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<VenueAccess>>([access]);
    }

    private sealed class FakeFieldCipher : IFieldCipher
    {
        public ValueTask<byte[]> EncryptAsync(string plaintext, FieldEncryptionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Encoding.UTF8.GetBytes($"{context.ToAssociatedData()}#{Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext))}"));

        public ValueTask<string> DecryptAsync(byte[] ciphertext, FieldEncryptionContext context, CancellationToken cancellationToken = default)
        {
            var parts = Encoding.UTF8.GetString(ciphertext).Split('#');
            return parts[0] == context.ToAssociatedData()
                ? ValueTask.FromResult(Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])))
                : throw new CryptographicException("context mismatch");
        }

        public ValueTask<bool> NeedsReEncryptionAsync(byte[] ciphertext, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class FakeBlindIndex : IBlindIndexService
    {
        private static readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        public byte[] Compute(string normalizedValue, string purpose) => HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{purpose}|{normalizedValue}"));
    }

    private sealed class FakeUsers : IUserAccountsContract
    {
        public string? NormalizePhone(string phoneNumber) => phoneNumber;
        public Task<bool> MatchesContactAsync(Guid userId, string? email, string? normalizedPhone, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.ToDictionary(id => id, _ => "Player Name"));
        public Task GrantRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendStaffInvitationAsync(string? email, string? normalizedPhone, string venueName, string role, string rawToken, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeCapacityReader : IVenueCapacityReader
    {
        public Task<IReadOnlyDictionary<DateOnly, int>> GetOpenMinutesAsync(
            Guid venueId, DateOnly from, DateOnly to, Guid? pitchId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<DateOnly, int>>(new Dictionary<DateOnly, int>());

        public Task<IReadOnlyList<PitchOpenMinutes>> GetOpenMinutesByPitchAsync(
            Guid venueId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PitchOpenMinutes>>([]);
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => _now.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
