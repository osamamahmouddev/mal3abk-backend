using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Malabak.Modules.Booking.Application.Customers;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;
using BookingEntity = Malabak.Modules.Booking.Domain.Booking;

namespace Malabak.UnitTests;

public sealed class VenueCustomerTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    private static readonly DateTime _now = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
    private const string _walkInPhone = "+201012345678";

    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _otherVenueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();
    private readonly Guid _karim = Guid.NewGuid();
    private readonly Guid _omar = Guid.NewGuid();
    private readonly Guid _staff = Guid.NewGuid();
    private readonly Guid _otherStaff = Guid.NewGuid();
    private readonly FakeCipher _cipher = new();

    [Fact]
    public async Task Directory_ListsPlayersAndWalkIns_WithCountsAndSpend_OnlyForThisVenueAsync()
    {
        await using var db = await SeedAsync();

        var page = await Service(db).ListAsync(_venueId, null, 1, 20);

        page.TotalCount.Should().Be(3, "the other venue's customer is not listed");
        var karim = page.Items.Single(c => c.Name == "Karim Adel");
        karim.Should().Match<CustomerSummaryDto>(c => c.IsRegisteredPlayer && c.Phone == null && c.TotalBookings == 2 && c.Completed == 1
            && c.Cancelled == 1 && c.TotalSpent == 400 && c.CustomerKey == $"player:{_karim:N}");
        page.Items.Single(c => c.Name == "Omar Khaled").Upcoming.Should().Be(1);

        var walkIn = page.Items.Single(c => !c.IsRegisteredPlayer);
        walkIn.Should().Match<CustomerSummaryDto>(c => c.Name == "Fathi" && c.Phone == _walkInPhone && c.NoShows == 1 && c.TotalBookings == 2);
        walkIn.CustomerKey.Should().StartWith("phone:").And.NotContain("1012345678", "the key goes in URLs, so it must not reveal the number");
    }

    [Fact]
    public async Task Search_MatchesNameOrPhone_AndPagesAsync()
    {
        await using var db = await SeedAsync();
        var service = Service(db);

        (await service.ListAsync(_venueId, "karim", 1, 20)).Items.Should().ContainSingle().Which.Name.Should().Be("Karim Adel");
        (await service.ListAsync(_venueId, "0123456", 1, 20)).Items.Should().ContainSingle().Which.Name.Should().Be("Fathi");
        var paged = await service.ListAsync(_venueId, null, 2, 2);
        paged.Items.Should().ContainSingle();
        paged.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Details_ShowOnlyThisCustomersBookingsAtThisVenue_AndHideOtherVenuesCustomersAsync()
    {
        await using var db = await SeedAsync();
        var service = Service(db);

        var detail = (await service.GetAsync(_venueId, $"player:{_karim:N}", _staff, false)).Value;

        detail.Bookings.Should().HaveCount(2).And.OnlyContain(b => b.Reference.StartsWith("K-", StringComparison.Ordinal));
        (await service.GetAsync(_venueId, $"player:{Guid.NewGuid():N}", _staff, false)).Error.Should().Be(CustomerErrors.NotFound);
        (await service.GetAsync(_otherVenueId, $"player:{_omar:N}", _staff, false)).Error.Should().Be(CustomerErrors.NotFound,
            "Omar never booked at the other venue, so its staff cannot look him up");
        (await service.GetAsync(_otherVenueId, $"player:{_karim:N}", _staff, false)).Value.Bookings.Select(b => b.Reference)
            .Should().Equal(["X-1"], "each venue only sees the bookings made with it");
        (await service.GetAsync(_venueId, "phone:not-a-real-key", _staff, false)).Error.Should().Be(CustomerErrors.NotFound);
    }

    [Fact]
    public async Task Notes_AreStoredEncrypted_AndOnlyTheAuthorOrAManagerCanDeleteThemAsync()
    {
        await using var db = await SeedAsync();
        var service = Service(db);
        var key = $"player:{_karim:N}";

        var added = (await service.AddNoteAsync(_venueId, key, _staff, "  Prefers pitch 2, pays cash.  ")).Value;

        added.Text.Should().Be("Prefers pitch 2, pays cash.");
        var stored = await db.CustomerNotes.SingleAsync();
        Encoding.UTF8.GetString(stored.TextCiphertext).Should().NotContain("Prefers", "the text is encrypted at rest");

        var seenByOther = (await service.GetAsync(_venueId, key, _otherStaff, canDeleteAnyNote: false)).Value.Notes.Single();
        seenByOther.Text.Should().Be("Prefers pitch 2, pays cash.");
        seenByOther.CanDelete.Should().BeFalse();

        (await service.DeleteNoteAsync(_venueId, key, added.Id, _otherStaff, canDeleteAnyNote: false)).Error.Should().Be(CustomerErrors.CannotDeleteNote);
        (await service.DeleteNoteAsync(_otherVenueId, key, added.Id, _staff, canDeleteAnyNote: true)).Error.Should().Be(CustomerErrors.NoteNotFound);
        (await service.DeleteNoteAsync(_venueId, key, added.Id, _otherStaff, canDeleteAnyNote: true)).IsSuccess.Should().BeTrue();
        (await service.GetAsync(_venueId, key, _staff, false)).Value.Notes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyOrOverlongNotes_AndNotesForStrangers_AreRejectedAsync(string text)
    {
        await using var db = await SeedAsync();
        var service = Service(db);

        (await service.AddNoteAsync(_venueId, $"player:{_karim:N}", _staff, text)).Error.Should().Be(CustomerErrors.InvalidNote);
        (await service.AddNoteAsync(_venueId, $"player:{_karim:N}", _staff, new string('x', 1001))).Error.Should().Be(CustomerErrors.InvalidNote);
        (await service.AddNoteAsync(_venueId, $"player:{Guid.NewGuid():N}", _staff, "hello")).Error.Should().Be(CustomerErrors.NotFound);
    }

    private VenueCustomerService Service(BookingDbContext db) => new(db, new FakeUsers(_karim, _omar), new FakeBlindIndex(), _cipher, new FixedClock());

    private static DateTime Local(int day, int hour) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, day, 0, 0, 0, DateTimeKind.Unspecified).AddHours(hour), _cairo);

    private async Task<BookingDbContext> SeedAsync()
    {
        var db = new BookingDbContext(new DbContextOptionsBuilder<BookingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var earlier = _now.AddDays(-10);

        var completed = Online("K-1", _karim, _venueId, Local(1, 19), earlier);
        completed.SubmitProof(Guid.NewGuid(), "p", earlier).IsSuccess.Should().BeTrue();
        completed.Confirm("s", earlier).IsSuccess.Should().BeTrue();
        completed.Complete(Local(1, 21)).IsSuccess.Should().BeTrue();

        var cancelled = Online("K-2", _karim, _venueId, Local(8, 19), earlier);
        cancelled.Cancel("busy", "p", false, earlier).IsSuccess.Should().BeTrue();

        var upcoming = Online("O-1", _omar, _venueId, Local(9, 19), earlier);
        upcoming.SubmitProof(Guid.NewGuid(), "p", earlier).IsSuccess.Should().BeTrue();
        upcoming.Confirm("s", earlier).IsSuccess.Should().BeTrue();

        var walkIn = BookingEntity.CreateManual(Guid.NewGuid(), "W-1", _venueId, _pitchId, _staff, "Fathi", _walkInPhone,
            Local(2, 19), Local(2, 20), Money.From(300), Money.From(0), true, null, earlier).Value;
        var walkInNoShow = BookingEntity.CreateManual(Guid.NewGuid(), "W-2", _venueId, _pitchId, _staff, "Fathi", _walkInPhone,
            Local(3, 19), Local(3, 20), Money.From(300), Money.From(0), true, null, earlier).Value;
        walkInNoShow.MarkNoShow("did not come", "s", Local(3, 20)).IsSuccess.Should().BeTrue();

        var elsewhere = Online("X-1", _karim, _otherVenueId, Local(4, 19), earlier);

        db.Bookings.AddRange(completed, cancelled, upcoming, walkIn, walkInNoShow, elsewhere);
        await db.SaveChangesAsync();
        return db;
    }

    private BookingEntity Online(string reference, Guid player, Guid venue, DateTime start, DateTime created) =>
        BookingEntity.Create(Guid.NewGuid(), reference, venue, _pitchId, player, start, start.AddHours(1),
            Money.From(400), Money.From(100), created.AddDays(30), null, null, created, "p").Value;

    private sealed class FakeUsers(Guid karim, Guid omar) : IUserAccountsContract
    {
        public string? NormalizePhone(string phoneNumber) => phoneNumber.Trim();

        public Task<bool> MatchesContactAsync(Guid userId, string? email, string? normalizedPhone, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.ToDictionary(id => id, Name));

        private string Name(Guid id)
        {
            if (id == karim)
            {
                return "Karim Adel";
            }

            return id == omar ? "Omar Khaled" : "Staff";
        }

        public Task GrantRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendStaffInvitationAsync(string? email, string? normalizedPhone, string venueName, string role, string rawToken,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeBlindIndex : IBlindIndexService
    {
        private static readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

        public byte[] Compute(string normalizedValue, string purpose) => HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{purpose}|{normalizedValue}"));
    }

    /// <summary>Reversible stand-in that still binds the ciphertext to its context, like AES-GCM associated data.</summary>
    private sealed class FakeCipher : IFieldCipher
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

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(_now, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
