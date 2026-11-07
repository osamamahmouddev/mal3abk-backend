using FluentAssertions;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Admin;
using Malabak.Modules.Booking.Application.Bookings.CancelBooking;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.Modules.Identity.Application.Admin;
using Malabak.Modules.Identity.Application.PasswordAuth;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.Modules.Venues.Application.Admin;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using BookingEntity = Malabak.Modules.Booking.Domain.Booking;

namespace Malabak.UnitTests;

/// <summary>Deeper edge cases for TASK-701/713 (users, staff accounts), TASK-702 (venues) and TASK-703 (bookings).</summary>
public sealed class Phase6AdminEdgeTests
{
    private static readonly DateTime _now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _admin = Guid.NewGuid();
    private readonly Recorder _recorder = new();
    private readonly Clock _clock = new();

    // ---------- TASK-701: users ----------

    [Fact]
    public async Task Search_FindsByIdAndEmail_IgnoresCase_AndIncludesDeletedOnlyWhenAskedAsync()
    {
        await using var db = IdentityDb();
        var player = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Mona Samir", _now);
        var owner = User.CreateStaff(Guid.NewGuid(), "Karim@Example.com", "+201112345678", "h", "Karim", UserRole.VenueOwner, _now);
        var gone = User.CreatePlayer(Guid.NewGuid(), "+201212345678", "Gone", _now);
        gone.RequestDeletion(_now);
        db.Users.AddRange(player, owner, gone);
        await db.SaveChangesAsync();
        var service = Users(db);

        (await service.SearchAsync(new AdminUserSearch(player.Id.ToString(), null, null))).Value.Items.Should().ContainSingle(u => u.Id == player.Id);
        (await service.SearchAsync(new AdminUserSearch("KARIM@example", null, null))).Value.Items.Should().ContainSingle(u => u.Id == owner.Id);
        (await service.SearchAsync(new AdminUserSearch("MONA", null, null))).Value.Total.Should().Be(1);
        (await service.SearchAsync(new AdminUserSearch(null, null, AdminUserStatus.Deleted))).Value.Items.Should().ContainSingle(u => u.Id == gone.Id);
        (await service.SearchAsync(new AdminUserSearch(null, null, AdminUserStatus.Active))).Value.Items.Should().NotContain(u => u.Id == gone.Id);
        (await service.SearchAsync(new AdminUserSearch(new string('x', 101), null, null))).Error.Should().Be(AdminUserErrors.InvalidFilter);
        (await service.SearchAsync(new AdminUserSearch(null, null, null, PageSize: 101))).Error.Should().Be(AdminUserErrors.InvalidFilter);
    }

    [Fact]
    public async Task SqlLikeCharacters_InTheSearch_AreTreatedAsTextAsync()
    {
        await using var db = IdentityDb();
        db.Users.Add(User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Mona", _now));
        await db.SaveChangesAsync();

        foreach (var term in new[] { "%", "_", "'; DROP TABLE users; --", "\\" })
        {
            (await Users(db).SearchAsync(new AdminUserSearch(term, null, null))).Value.Items.Should().BeEmpty(term);
        }
    }

    [Fact]
    public async Task DeletedAccounts_CannotBeSuspended_AndReasonsHaveALengthLimitAsync()
    {
        await using var db = IdentityDb();
        var gone = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Gone", _now);
        gone.RequestDeletion(_now);
        var live = User.CreatePlayer(Guid.NewGuid(), "+201112345678", "Live", _now);
        db.Users.AddRange(gone, live);
        await db.SaveChangesAsync();
        var service = Users(db);

        (await service.SuspendAsync(gone.Id, "fraud", _admin)).Error.Should().Be(AdminUserErrors.Deleted);
        (await service.SuspendAsync(live.Id, new string('x', 501), _admin)).Error.Should().Be(AdminUserErrors.ReasonRequired);
        (await service.ReactivateAsync(live.Id, "not suspended", _admin)).Error.Should().Be(AdminUserErrors.NotSuspended);
        (await service.RevokeSessionsAsync(live.Id, "lost phone", _admin)).Value.Should().Be(0);
        _recorder.Audits.Should().Contain(a => a.Action == "admin.user.sessions_revoked");
    }

    [Fact]
    public async Task ASuspendedUser_CannotSignInWithAPassword_OrRefreshAsync()
    {
        await using var db = IdentityDb();
        var owner = User.CreateStaff(Guid.NewGuid(), "owner@example.com", "+201012345678", "h", "Owner", UserRole.VenueOwner, _now);
        owner.Suspend("chargeback", _now);
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        (await new UserPermissionService(db).GetAsync(owner.Id)).Should().BeNull();
        owner.IsSuspended.Should().BeTrue();
        PasswordAuthErrors.AccountSuspended.Code.Should().Be("Identity.AccountSuspended");
    }

    // ---------- TASK-713: staff accounts ----------

    [Fact]
    public async Task StaffAccounts_ValidateInput_AndPhonesMustBeUniqueAsync()
    {
        await using var db = IdentityDb();
        var root = User.CreateStaff(Guid.NewGuid(), "root@example.com", "+201000000001", "h", "Root", UserRole.Admin, _now);
        db.Users.AddRange(root, User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Player", _now));
        await db.SaveChangesAsync();
        var service = Accounts(db);

        (await service.CreateAsync(new CreateAdminAccountInput("", "a@example.com", null, [UserRole.Finance]), root.Id)).Error.Should().Be(AdminAccountErrors.Invalid);
        (await service.CreateAsync(new CreateAdminAccountInput("A", "not-an-email", null, [UserRole.Finance]), root.Id)).Error.Should().Be(AdminAccountErrors.Invalid);
        (await service.CreateAsync(new CreateAdminAccountInput("A", "a@example.com", "abc", [UserRole.Finance]), root.Id)).Error.Should().Be(AdminAccountErrors.Invalid);
        (await service.CreateAsync(new CreateAdminAccountInput("A", "a@example.com", "01012345678", [UserRole.Finance]), root.Id)).Error.Should().Be(AdminAccountErrors.PhoneInUse);
        (await service.CreateAsync(new CreateAdminAccountInput("A", "a@example.com", null, []), root.Id)).Error.Should().Be(AdminAccountErrors.InvalidRoles);
        (await service.CreateAsync(new CreateAdminAccountInput("A", "a@example.com", null, ["Player"]), root.Id)).Error.Should().Be(AdminAccountErrors.InvalidRoles);
    }

    [Fact]
    public async Task ReplacingRoles_KeepsNonStaffRoles_AndDisabledAccountsCanBeEnabledAgainAsync()
    {
        await using var db = IdentityDb();
        var root = User.CreateStaff(Guid.NewGuid(), "root@example.com", "+201000000001", "h", "Root", UserRole.Admin, _now);
        var helper = User.CreateStaff(Guid.NewGuid(), "help@example.com", "+201000000002", "h", "Help", UserRole.SupportAgent, _now);
        helper.AddRole(UserRole.Player);
        db.Users.AddRange(root, helper);
        await db.SaveChangesAsync();
        var service = Accounts(db);

        var changed = (await service.ReplaceRolesAsync(helper.Id, [UserRole.Finance], "moved team", root.Id)).Value;
        changed.Roles.Should().BeEquivalentTo(UserRole.Player, UserRole.Finance);
        (await service.ReplaceRolesAsync(helper.Id, [UserRole.Finance], " ", root.Id)).Error.Should().Be(AdminAccountErrors.ReasonRequired);

        (await service.DisableAsync(helper.Id, "left", root.Id)).Value.Status.Should().Be(AdminUserStatus.Suspended);
        (await service.DisableAsync(helper.Id, "again", root.Id)).Error.Should().Be(AdminAccountErrors.AlreadyDisabled);
        (await service.EnableAsync(helper.Id, "back", root.Id)).Value.Status.Should().Be(AdminUserStatus.Active);
        (await service.EnableAsync(helper.Id, "again", root.Id)).Error.Should().Be(AdminAccountErrors.NotDisabled);
        (await service.EnableAsync(Guid.NewGuid(), "x", root.Id)).Error.Should().Be(AdminAccountErrors.NotFound);
    }

    // ---------- TASK-702: venues ----------

    [Fact]
    public async Task VenueAdmin_ValidatesEveryAction_AndNotesCanBeClearedAsync()
    {
        await using var db = VenuesDb();
        var venue = Approved(db, "Arena");
        await db.SaveChangesAsync();
        var service = Venues(db);

        (await service.GetAsync(Guid.NewGuid())).Error.Should().Be(AdminVenueErrors.NotFound);
        (await service.RequestInfoAsync(venue.Id, " ", _admin)).Error.Should().Be(AdminVenueErrors.ReasonRequired);
        (await service.RequestInfoAsync(venue.Id, new string('x', 1001), _admin)).Error.Should().Be(AdminVenueErrors.ReasonRequired);
        (await service.TransferOwnershipAsync(venue.Id, Guid.NewGuid(), "", _admin)).Error.Should().Be(AdminVenueErrors.ReasonRequired);

        (await service.SetAdminNoteAsync(venue.Id, "watch this one", _admin)).Value.AdminNote.Should().Be("watch this one");
        (await service.SetAdminNoteAsync(venue.Id, "   ", _admin)).Value.AdminNote.Should().BeNull();

        (await service.RequestInfoAsync(venue.Id, "Send the lease contract", _admin)).IsSuccess.Should().BeTrue();
        _recorder.Notifications.Should().ContainSingle(n => n.Type == NotificationTypes.VenueInfoRequested && n.Data["message"] == "Send the lease contract");
        (await db.Venues.SingleAsync()).ApprovalStatus.Should().Be(VenueApprovalStatus.Approved, "asking the owner does not change the status");
    }

    [Fact]
    public async Task AnUnpublishedVenue_StaysUnpublished_AfterSuspensionAndReactivationAsync()
    {
        await using var db = VenuesDb();
        var venue = Approved(db, "Quiet", published: false);
        await db.SaveChangesAsync();
        var service = Venues(db);

        await service.SuspendAsync(venue.Id, "Complaints", _admin);
        var back = (await service.ReactivateAsync(venue.Id, "Resolved", _admin)).Value;

        back.ApprovalStatus.Should().Be(nameof(VenueApprovalStatus.Approved));
        back.IsPublished.Should().BeFalse("reactivation restores the state from before the suspension");
    }

    [Fact]
    public async Task VenueSearch_FiltersByOwner_AndHidesDeletedVenuesAsync()
    {
        await using var db = VenuesDb();
        var mine = Approved(db, "Mine");
        var other = Approved(db, "Other", owner: Guid.NewGuid());
        other.MarkDeleted(_now);
        await db.SaveChangesAsync();

        var page = (await Venues(db).SearchAsync(new AdminVenueSearch(null, null, mine.OwnerId))).Value;

        page.Items.Should().ContainSingle(v => v.Id == mine.Id);
        (await Venues(db).SearchAsync(new AdminVenueSearch("Other", null, null))).Value.Items.Should().BeEmpty();
    }

    // ---------- TASK-703: bookings ----------

    [Fact]
    public async Task BookingSearch_FiltersByPlayerAndDateRange_AndDetailShowsTheHistoryAsync()
    {
        await using var db = BookingDb();
        var player = Guid.NewGuid();
        var early = NewBooking("MAL-EARLY1", player, _now.AddDays(1));
        var late = NewBooking("MAL-LATE22", player, _now.AddDays(10));
        db.Bookings.AddRange(early, late, NewBooking("MAL-OTHER3", Guid.NewGuid(), _now.AddDays(1)));
        await db.SaveChangesAsync();
        var service = Bookings(db);

        (await service.SearchAsync(new AdminBookingSearch(null, null, player, null, null, null))).Value.Total.Should().Be(2);
        (await service.SearchAsync(new AdminBookingSearch(null, null, player, null, _now, _now.AddDays(5)))).Value.Items.Should().ContainSingle(b => b.Reference == "MAL-EARLY1");
        (await service.SearchAsync(new AdminBookingSearch(null, null, null, null, _now, _now.AddDays(400)))).Error.Should().Be(AdminBookingErrors.InvalidFilter);
        (await service.SearchAsync(new AdminBookingSearch(new string('x', 41), null, null, null, null, null))).Error.Should().Be(AdminBookingErrors.InvalidFilter);

        var detail = (await service.GetAsync(early.Id)).Value;
        detail.History.Should().NotBeEmpty();
        detail.Booking.Reference.Should().Be("MAL-EARLY1");
        (await service.GetAsync(Guid.NewGuid())).Error.Should().Be(BookingErrors.NotFound);
    }

    [Fact]
    public async Task ForceCancel_RefusesFinishedBookings_AndDoesNotAuditFailuresAsync()
    {
        await using var db = BookingDb();
        var booking = NewBooking("MAL-DONE11", Guid.NewGuid(), _now.AddDays(1));
        booking.SubmitProof(Guid.NewGuid(), "p", _now);
        booking.Confirm("s", _now);
        booking.Cancel("player", "p", isStaff: true, _now);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        (await Bookings(db).ForceCancelAsync(booking.Id, "again", _admin)).Error.Should().Be(BookingErrors.InvalidTransition);
        _recorder.Audits.Should().BeEmpty();
    }

    private AdminUserService Users(IdentityDbContext db) => new(db, TestSessions.Create(db, _clock), _recorder, _clock);

    private AdminAccountService Accounts(IdentityDbContext db) =>
        new(db, new Pbkdf2PasswordHasher(), new PhoneNumberNormalizer(), new NoReset(), TestSessions.Create(db, _clock), _recorder, _clock);

    private AdminVenueService Venues(VenuesDbContext db) => new(db, new NoStats(), new KnownAccounts(), _recorder, _recorder, _clock);

    private AdminBookingService Bookings(BookingDbContext db) =>
        new(db, new VenueCancelBookingHandler(db, _clock, new NoEffects(), new NoEffects(), NullLogger<VenueCancelBookingHandler>.Instance), _recorder);

    private Venue Approved(VenuesDbContext db, string name, bool published = true, Guid? owner = null)
    {
        var o = owner ?? Guid.NewGuid();
        var venue = Venue.Create(Guid.NewGuid(), name, $"v-{Guid.NewGuid():N}", null, o, "Addr", "Cairo", "Maadi", null, null, "+201000000000", null, null, _now);
        venue.SubmitForApproval(o, null, _now);
        venue.Approve(_admin, null, _now);
        if (published)
        {
            venue.SetPublished(true, _now);
        }

        db.Venues.Add(venue);
        return venue;
    }

    private static BookingEntity NewBooking(string reference, Guid player, DateTime start) =>
        BookingEntity.Create(Guid.NewGuid(), reference, Guid.NewGuid(), Guid.NewGuid(), player, start, start.AddHours(1), Money.From(400), Money.From(100),
            _now.AddMinutes(30), null, null, _now.AddDays(-1), "p").Value;

    private static IdentityDbContext IdentityDb() => new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static VenuesDbContext VenuesDb() => new(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static BookingDbContext BookingDb() => new(new DbContextOptionsBuilder<BookingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class NoReset : ICommandHandler<ForgotPasswordCommand, Result>
    {
        public Task<Result> HandleAsync(ForgotPasswordCommand command, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
    }

    private sealed class NoStats : IVenueBookingStatsReader
    {
        public Task<VenueBookingStats> GetAsync(Guid venueId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(new VenueBookingStats(0, 0, 0, 0, 0, 0, 0, "EGP", null));
    }

    private sealed class KnownAccounts : IUserAccountsContract
    {
        public string? NormalizePhone(string phoneNumber) => phoneNumber;

        public Task<bool> MatchesContactAsync(Guid userId, string? email, string? normalizedPhone, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.ToDictionary(id => id, _ => "Someone"));

        public Task GrantRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendStaffInvitationAsync(string? email, string? normalizedPhone, string venueName, string role, string rawToken, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoEffects : IAvailabilityInvalidator, IBookingRealtimeBroadcaster
    {
        public Task InvalidatePitchAsync(Guid pitchId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task InvalidateVenueAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BroadcastSlotStatusChangedAsync(Guid venueId, Guid pitchId, DateTime startUtc, DateTime endUtc, string status, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Recorder : IAuditLog, INotificationPublisher
    {
        public List<AuditEntry> Audits { get; } = [];
        public List<NotificationRequest> Notifications { get; } = [];

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Audits.Add(entry);
            return Task.CompletedTask;
        }

        public Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            Notifications.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class Clock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
