using FluentAssertions;
using Malabak.Modules.Identity.Application.Crm;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>The admin CRM: contacts are one per phone number, encrypted, move through stages and keep a timeline.</summary>
public sealed class CrmTests
{
    private static readonly DateTime _now = new(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc);
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Audit _audit = new();
    private readonly Clock _clock = new();

    [Fact]
    public async Task ACreatedContact_StartsNew_WithItsPhoneEncryptedAndFoundByBlindIndexAsync()
    {
        await using var db = NewDb();

        var created = await Service(db).CreateAsync(Input("كابتن أحمد", "01012345678", venue: "ملعب النصر"), _admin);

        created.IsSuccess.Should().BeTrue();
        created.Value.Contact.Stage.Should().Be(nameof(CrmStage.New));
        created.Value.Contact.Phone.Should().Be("+201012345678");
        var stored = await db.CrmContacts.SingleAsync();
        System.Text.Encoding.UTF8.GetString(stored.PhoneEncrypted).Should().StartWith("ENC:", "the number is never stored in plain text");
        stored.PhoneBlindIndex.Should().HaveLength(64);
        _audit.Entries.Should().ContainSingle(e => e.Action == "admin.crm.contact_created");
    }

    [Fact]
    public async Task TheSamePhoneTwice_IsADuplicate_WhateverWayItIsWrittenAsync()
    {
        await using var db = NewDb();
        await Service(db).CreateAsync(Input("أحمد", "01012345678"), _admin);

        var again = await Service(db).CreateAsync(Input("Ahmed", "+20 101 234 5678"), _admin);

        again.Error.Should().Be(CrmErrors.DuplicatePhone);
    }

    [Theory]
    [InlineData("", "01012345678", "Crm.NameRequired")]
    [InlineData("أحمد", "123", null)]
    public async Task InvalidInput_IsRefusedAsync(string name, string phone, string? code)
    {
        await using var db = NewDb();

        var result = await Service(db).CreateAsync(Input(name, phone), _admin);

        result.IsFailure.Should().BeTrue();
        if (code is not null)
        {
            result.Error.Code.Should().Be(code);
        }

        (await db.CrmContacts.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task MovingStages_IsKeptOnTheTimeline_AndLostNeedsAReasonAsync()
    {
        await using var db = NewDb();
        var id = (await Service(db).CreateAsync(Input("أحمد", "01012345678", followUp: _now.AddDays(1)), _admin)).Value.Contact.Id;

        (await Service(db).MoveAsync(id, CrmStage.Contacted, null, _admin)).IsSuccess.Should().BeTrue();
        var noReason = await Service(db).MoveAsync(id, CrmStage.Lost, "  ", _admin);
        var lost = await Service(db).MoveAsync(id, CrmStage.Lost, "عنده نظام حجز تاني", _admin);

        noReason.Error.Should().Be(CrmErrors.LostReasonRequired);
        lost.Value.Contact.Stage.Should().Be(nameof(CrmStage.Lost));
        lost.Value.Contact.LostReason.Should().Be("عنده نظام حجز تاني");
        lost.Value.Contact.NextFollowUpAtUtc.Should().BeNull("nobody chases a lost venue");
        lost.Value.Notes.Where(n => n.Kind == nameof(CrmNoteKind.StageChange)).Should().HaveCount(2);
        _audit.Entries.Count(e => e.Action == "admin.crm.stage_changed").Should().Be(2);
    }

    [Fact]
    public async Task NotesAndCalls_AreAdded_NewestFirst_AndCannotBeEmptyAsync()
    {
        await using var db = NewDb();
        var id = (await Service(db).CreateAsync(Input("أحمد", "01012345678"), _admin)).Value.Contact.Id;

        await Service(db).AddNoteAsync(id, CrmNoteKind.Call, "كلمته، هيبعت صور الملعب", _admin);
        _clock.UtcNow = _now.AddMinutes(5);
        var detail = await Service(db).AddNoteAsync(id, CrmNoteKind.Note, "عنده ٣ ملاعب خماسي", _admin);
        var empty = await Service(db).AddNoteAsync(id, CrmNoteKind.Note, "   ", _admin);
        var fakeStage = await Service(db).AddNoteAsync(id, CrmNoteKind.StageChange, "New → Active", _admin);

        detail.Value.Notes.Select(n => n.Kind).Should().Equal(nameof(CrmNoteKind.Note), nameof(CrmNoteKind.Call));
        empty.Error.Should().Be(CrmErrors.NoteRequired);
        fakeStage.Error.Should().Be(CrmErrors.InvalidNoteKind, "a stage change can only come from an actual move");
    }

    [Fact]
    public async Task ImportingTheWaitlist_AddsVenueOwnersOnce_AndSkipsPlayersAndKnownNumbersAsync()
    {
        await using var db = NewDb();
        var cipher = new Cipher();
        await AddWaitlistAsync(db, cipher, WaitlistAudience.Venue, "+201011111111");
        await AddWaitlistAsync(db, cipher, WaitlistAudience.Venue, "+201022222222");
        await AddWaitlistAsync(db, cipher, WaitlistAudience.Player, "+201033333333");
        await Service(db).CreateAsync(Input("موجود", "01022222222"), _admin);

        var first = await Service(db).ImportWaitlistAsync(_admin);
        var second = await Service(db).ImportWaitlistAsync(_admin);

        first.Should().Be(new CrmImportResult(1, 1));
        second.Should().Be(new CrmImportResult(0, 1), "the known number stays skipped and nothing is imported twice");
        var imported = await db.CrmContacts.SingleAsync(c => c.Source == CrmSource.Waitlist);
        imported.WaitlistEntryId.Should().NotBeNull();
        (await Service(db).GetSummaryAsync()).ByStage[nameof(CrmStage.New)].Should().Be(2);
    }

    [Fact]
    public async Task TheSummary_CountsFollowUpsDueByTheEndOfTheCairoDayAsync()
    {
        await using var db = NewDb();
        await Service(db).CreateAsync(Input("متأخر", "01011111111", followUp: _now.AddDays(-2)), _admin);
        await Service(db).CreateAsync(Input("النهارده", "01022222222", followUp: _now.AddHours(5)), _admin);
        await Service(db).CreateAsync(Input("بكره", "01033333333", followUp: _now.AddDays(2)), _admin);

        var summary = await Service(db).GetSummaryAsync();

        summary.FollowUpsDue.Should().Be(2);
        summary.NewThisWeek.Should().Be(3);
    }

    private CrmService Service(IdentityDbContext db) =>
        new(db, new PhoneNumberNormalizer(), new BlindIndex(), new Cipher(), _clock, _audit, new Names());

    private static CrmContactInput Input(string name, string phone, string? venue = null, DateTime? followUp = null) =>
        new(name, venue, null, phone, followUp, null);

    private static async Task AddWaitlistAsync(IdentityDbContext db, IFieldCipher cipher, WaitlistAudience audience, string phone)
    {
        var id = Guid.NewGuid();
        var encrypted = await cipher.EncryptAsync(phone, new FieldEncryptionContext("waitlist_entries", "phone", id));
        db.WaitlistEntries.Add(WaitlistEntry.Create(id, audience, null, encrypted, Guid.NewGuid().ToString("N"), null, null, _now));
        await db.SaveChangesAsync();
    }

    private static IdentityDbContext NewDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Audit : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class Names : IUserAccountsContract
    {
        public string? NormalizePhone(string phoneNumber) => phoneNumber;

        public Task<bool> MatchesContactAsync(Guid userId, string? email, string? normalizedPhone, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.ToDictionary(id => id, _ => "Admin"));

        public Task GrantRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendStaffInvitationAsync(string? email, string? normalizedPhone, string venueName, string role, string rawToken,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Clock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        public DateTime UtcNow { get; set; } = _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }

    private sealed class Cipher : IFieldCipher
    {
        public ValueTask<byte[]> EncryptAsync(string plaintext, FieldEncryptionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(System.Text.Encoding.UTF8.GetBytes($"ENC:{context.RowId}:{plaintext}"));

        public ValueTask<string> DecryptAsync(byte[] ciphertext, FieldEncryptionContext context, CancellationToken cancellationToken = default)
        {
            var parts = System.Text.Encoding.UTF8.GetString(ciphertext)[4..].Split(':', 2);
            if (!Guid.TryParse(parts[0], out var rowId) || rowId != context.RowId)
            {
                throw new System.Security.Cryptography.CryptographicException("AAD mismatch");
            }

            return ValueTask.FromResult(parts[1]);
        }

        public ValueTask<bool> NeedsReEncryptionAsync(byte[] ciphertext, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class BlindIndex : IBlindIndexService
    {
        public byte[] Compute(string normalizedValue, string purpose) =>
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(purpose + ":" + normalizedValue));
    }
}
