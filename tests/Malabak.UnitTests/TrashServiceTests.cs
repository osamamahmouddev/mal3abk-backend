using FluentAssertions;
using Malabak.Api.Infrastructure.Platform;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>The admin trash: delete with a reason, restore, purge only with the name typed, and automatic purge after 30 days.</summary>
public sealed class TrashServiceTests
{
    private static readonly DateTime _now = new(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc);
    private readonly Guid _admin = Guid.NewGuid();
    private readonly FakeProvider _venues = new("venue", "ملعب النصر");
    private readonly FakeProvider _users = new("user", "أحمد علي");
    private readonly Audit _audit = new();
    private readonly Clock _clock = new();

    [Fact]
    public async Task Deleting_NeedsAReason_AndKeepsWhoDeletedItAndWhenItIsPurgedAsync()
    {
        await using var db = Db();
        var id = Guid.NewGuid();

        var noReason = await Service(db).MoveToTrashAsync("user", id, " ab ", null, _admin);
        var deleted = await Service(db).MoveToTrashAsync("user", id, "حساب وهمي بيحجز ومبيجيش", null, _admin);

        noReason.Error.Should().Be(TrashErrors.ReasonRequired);
        _users.Trashed.Should().ContainSingle().Which.Should().Be(id);
        deleted.Value.Title.Should().Be("أحمد علي");
        deleted.Value.DeletedByName.Should().Be("Admin");
        deleted.Value.PurgeAtUtc.Should().Be(_now.AddDays(30));
        _audit.Actions.Should().Equal("admin.trash.deleted");
    }

    [Fact]
    public async Task AVenue_IsOnlyDeletedWithItsNameTypedAsync()
    {
        await using var db = Db();
        var id = Guid.NewGuid();

        var wrong = await Service(db).MoveToTrashAsync("venue", id, "ملعب مقفول نهائي", "ملعب تاني", _admin);
        var right = await Service(db).MoveToTrashAsync("venue", id, "ملعب مقفول نهائي", " ملعب النصر ", _admin);

        wrong.Error.Should().Be(TrashErrors.ConfirmationMismatch);
        right.IsSuccess.Should().BeTrue();
        _venues.Trashed.Should().ContainSingle("the wrong name deleted nothing");
    }

    [Fact]
    public async Task ARefusalFromTheModule_LeavesNothingInTheTrashAsync()
    {
        await using var db = Db();
        _users.Refusal = TrashErrors.HasUpcomingBookings(2);

        var result = await Service(db).MoveToTrashAsync("user", Guid.NewGuid(), "مش عاوزينه", null, _admin);

        result.Error.Code.Should().Be("Trash.HasUpcomingBookings");
        (await db.TrashEntries.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Restoring_PutsItBack_AndTakesItOutOfTheTrashAsync()
    {
        await using var db = Db();
        var id = Guid.NewGuid();
        var entry = (await Service(db).MoveToTrashAsync("user", id, "اتحذف بالغلط", null, _admin)).Value;

        (await Service(db).RestoreAsync(entry.Id, _admin)).IsSuccess.Should().BeTrue();

        _users.Restored.Should().Equal(id);
        (await Service(db).ListAsync(null)).Should().BeEmpty();
    }

    [Fact]
    public async Task Purging_NeedsTheNameTyped_AndIsFinalAsync()
    {
        await using var db = Db();
        var id = Guid.NewGuid();
        var entry = (await Service(db).MoveToTrashAsync("user", id, "طلب حذف بياناته", null, _admin)).Value;

        var wrong = await Service(db).PurgeAsync(entry.Id, "احمد", _admin);
        var right = await Service(db).PurgeAsync(entry.Id, "أحمد علي", _admin);

        wrong.Error.Should().Be(TrashErrors.ConfirmationMismatch);
        right.IsSuccess.Should().BeTrue();
        _users.Purged.Should().Equal(id);
        (await Service(db).RestoreAsync(entry.Id, _admin)).Error.Should().Be(TrashErrors.NotFound, "nothing purged can come back");
    }

    [Fact]
    public async Task EmptyingTheTrash_NeedsTheExactPhrase_AndPurgesEverythingAsync()
    {
        await using var db = Db();
        await Service(db).MoveToTrashAsync("user", Guid.NewGuid(), "حساب مكرر", null, _admin);
        await Service(db).MoveToTrashAsync("venue", Guid.NewGuid(), "ملعب مكرر", "ملعب النصر", _admin);

        var wrong = await Service(db).PurgeAllAsync("امسح", _admin);
        var all = await Service(db).PurgeAllAsync(TrashService.EmptyAllConfirmation, _admin);

        wrong.Error.Should().Be(TrashErrors.ConfirmationMismatch);
        all.Value.Should().Be(new TrashPurgeAllResult(2, 0));
        (await db.TrashEntries.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task AfterThirtyDays_ItemsArePurgedAutomatically_NewerOnesStayAsync()
    {
        await using var db = Db();
        var old = Guid.NewGuid();
        await Service(db).MoveToTrashAsync("user", old, "حساب قديم", null, _admin);
        _clock.UtcNow = _now.AddDays(20);
        await Service(db).MoveToTrashAsync("user", Guid.NewGuid(), "حساب جديد", null, _admin);
        _clock.UtcNow = _now.AddDays(30).AddMinutes(1);

        var result = await Service(db).PurgeExpiredAsync();

        result.Should().Be(new TrashPurgeAllResult(1, 0));
        _users.Purged.Should().Equal(old);
        (await Service(db).ListAsync(null)).Should().ContainSingle(e => e.Reason == "حساب جديد");
        _audit.Entries.Should().Contain(e => e.Action == "admin.trash.purged" && e.ActorType == AuditActorType.System);
    }

    [Fact]
    public async Task AnUnknownKind_IsRefusedAsync()
    {
        await using var db = Db();

        (await Service(db).MoveToTrashAsync("booking", Guid.NewGuid(), "مش مسموح", null, _admin)).Error.Should().Be(TrashErrors.UnknownKind);
    }

    private TrashService Service(PlatformDbContext db) =>
        new(db, [_venues, _users], _audit, _clock, new Names(), NullLogger<TrashService>.Instance);

    private static PlatformDbContext Db() =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeProvider(string kind, string name) : ITrashProvider
    {
        public List<Guid> Trashed { get; } = [];
        public List<Guid> Restored { get; } = [];
        public List<Guid> Purged { get; } = [];
        public Error? Refusal { get; set; }

        public string Kind => kind;

        public Task<string?> GetNameAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<string?>(name);

        public Task<Result<TrashedItem>> MoveToTrashAsync(Guid id, Guid adminId, CancellationToken cancellationToken = default)
        {
            if (Refusal is not null)
            {
                return Task.FromResult(Result.Failure<TrashedItem>(Refusal));
            }

            Trashed.Add(id);
            return Task.FromResult(Result.Success(new TrashedItem(name, null)));
        }

        public Task<Result> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Restored.Add(id);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> PurgeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Purged.Add(id);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class Audit : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = [];
        public IEnumerable<string> Actions => Entries.Select(e => e.Action);

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
}
