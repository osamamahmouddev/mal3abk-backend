using FluentAssertions;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Admin;
using Malabak.Modules.Booking.Application.Bookings.CancelBooking;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
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

public sealed class AdminVenueBookingTests
{
    private static readonly DateTime _now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _player = Guid.NewGuid();
    private readonly Recorder _recorder = new();
    private readonly Accounts _accounts = new();
    private readonly Clock _clock = new();

    public AdminVenueBookingTests()
    {
        _accounts.Known.Add(_owner);
    }

    [Fact]
    public async Task SuspendingAVenue_HidesIt_AndReactivationRestoresThePublishedStateAsync()
    {
        await using var db = VenuesDb();
        var venue = await ApprovedVenueAsync(db, published: true);
        var service = Venues(db);

        (await service.SuspendAsync(venue.Id, "", _admin)).Error.Should().Be(AdminVenueErrors.ReasonRequired);
        var suspended = (await service.SuspendAsync(venue.Id, "Fraud complaints", _admin)).Value;

        suspended.ApprovalStatus.Should().Be(nameof(VenueApprovalStatus.Suspended));
        suspended.IsPublished.Should().BeFalse();
        (await db.Venues.SingleAsync()).IsSearchable.Should().BeFalse("players can no longer find or book it");
        var owner = () => db.Venues.Single().SetPublished(true, _now);
        owner.Should().Throw<InvalidOperationException>("the owner cannot republish a suspended venue");
        _recorder.Notifications.Should().ContainSingle(n => n.Type == NotificationTypes.VenueSuspended && n.UserId == _owner);

        var back = (await service.ReactivateAsync(venue.Id, "Resolved", _admin)).Value;
        back.ApprovalStatus.Should().Be(nameof(VenueApprovalStatus.Approved));
        back.IsPublished.Should().BeTrue();
        back.History.Select(h => h.Action).Should().Contain([nameof(VenueApprovalAction.Reactivated), nameof(VenueApprovalAction.Suspended)]);
        _recorder.Audits.Select(a => a.Action).Should().Contain(["admin.venue.suspended", "admin.venue.reactivated"]);

        (await service.ReactivateAsync(venue.Id, "again", _admin)).IsFailure.Should().BeTrue("only suspended venues can be reactivated");
    }

    [Fact]
    public async Task OnlyApprovedVenues_CanBeSuspendedAsync()
    {
        await using var db = VenuesDb();
        var draft = Venue.Create(Guid.NewGuid(), "Draft", "draft", null, _owner, "Addr", "Cairo", "Nasr City", null, null, "+201000000000", null, null, _now);
        db.Venues.Add(draft);
        await db.SaveChangesAsync();

        (await Venues(db).SuspendAsync(draft.Id, "x", _admin)).Error.Code.Should().StartWith("VenueApproval.");
    }

    [Fact]
    public async Task TransferringOwnership_GrantsTheRole_RemovesTheirStaffSeat_AndNeedsAnExistingAccountAsync()
    {
        await using var db = VenuesDb();
        var venue = await ApprovedVenueAsync(db, published: true);
        var newOwner = Guid.NewGuid();
        db.StaffMembers.Add(VenueStaffMember.Create(venue.Id, newOwner, VenueStaffRole.Manager, _owner, _now));
        await db.SaveChangesAsync();
        var service = Venues(db);

        (await service.TransferOwnershipAsync(venue.Id, Guid.NewGuid(), "sold", _admin)).Error.Should().Be(AdminVenueErrors.OwnerNotFound);

        _accounts.Known.Add(newOwner);
        var moved = (await service.TransferOwnershipAsync(venue.Id, newOwner, "Venue sold", _admin)).Value;

        moved.OwnerId.Should().Be(newOwner);
        moved.StaffCount.Should().Be(0);
        _accounts.Granted.Should().ContainSingle(g => g.UserId == newOwner && g.Role == "VenueOwner");
        _recorder.Audits.Should().Contain(a => a.Action == "admin.venue.ownership_transferred" && a.Reason == "Venue sold");
        (await service.TransferOwnershipAsync(venue.Id, newOwner, "again", _admin)).IsFailure.Should().BeTrue("already the owner");
    }

    [Fact]
    public async Task VenueSearch_AndDetail_IncludePitchInventoryAndNotesAsync()
    {
        await using var db = VenuesDb();
        var venue = await ApprovedVenueAsync(db, published: false);
        db.Pitches.Add(Pitch.Create(Guid.NewGuid(), venue.Id, "Pitch A", "ArtificialTurf", 10, null, null, null, _now));
        await db.SaveChangesAsync();
        var service = Venues(db);

        var page = (await service.SearchAsync(new AdminVenueSearch("ZAMALEK", "approved", null))).Value;
        page.Items.Should().ContainSingle().Which.Pitches.Should().Be(1);
        (await service.SearchAsync(new AdminVenueSearch(null, "Banana", null))).Error.Should().Be(AdminVenueErrors.InvalidFilter);

        (await service.SetAdminNoteAsync(venue.Id, new string('x', Venue.MaxAdminNoteLength + 1), _admin)).Error.Should().Be(AdminVenueErrors.NoteTooLong);
        var detail = (await service.SetAdminNoteAsync(venue.Id, "  Owner prefers calls  ", _admin)).Value;
        detail.AdminNote.Should().Be("Owner prefers calls");
        detail.Pitches.Should().ContainSingle(p => p.Name == "Pitch A");
        detail.OwnerName.Should().Be("Owner");
    }

    [Fact]
    public async Task BookingSearch_FiltersAcrossVenues_AndRejectsBadFiltersAsync()
    {
        await using var db = BookingDb();
        var venueA = Guid.NewGuid();
        db.Bookings.AddRange(Online("MAL-AAA1", venueA, _now.AddDays(1)), Online("MAL-BBB2", Guid.NewGuid(), _now.AddDays(2)));
        await db.SaveChangesAsync();
        var service = Bookings(db);

        (await service.SearchAsync(new AdminBookingSearch(null, null, null, null, null, null))).Value.Total.Should().Be(2);
        (await service.SearchAsync(new AdminBookingSearch("aaa", null, null, null, null, null))).Value.Items.Should().ContainSingle(b => b.Reference == "MAL-AAA1");
        (await service.SearchAsync(new AdminBookingSearch(null, venueA, null, "pendingdeposit", null, null))).Value.Items.Should().ContainSingle();
        (await service.SearchAsync(new AdminBookingSearch(null, null, null, "Teleported", null, null))).Error.Should().Be(AdminBookingErrors.InvalidFilter);
        (await service.SearchAsync(new AdminBookingSearch(null, null, null, null, _now, _now.AddDays(-1)))).Error.Should().Be(AdminBookingErrors.InvalidFilter);
    }

    [Fact]
    public async Task ForceCancel_IgnoresThePlayerDeadline_OwesTheFullDeposit_AndIsAuditedAsync()
    {
        await using var db = BookingDb();
        var booking = Online("MAL-CCC3", Guid.NewGuid(), _now.AddHours(2), deposit: 150);
        booking.SubmitProof(Guid.NewGuid(), "p", _now).IsSuccess.Should().BeTrue();
        booking.Confirm("s", _now).IsSuccess.Should().BeTrue();
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        var service = Bookings(db);

        (await service.ForceCancelAsync(booking.Id, "  ", _admin)).Error.Should().Be(BookingErrors.ReasonRequired);
        var detail = (await service.ForceCancelAsync(booking.Id, "Venue closed by authorities", _admin)).Value;

        detail.Booking.Status.Should().Be(nameof(BookingStatus.Cancelled));
        detail.CancellationReason.Should().Be("[Malabak] Venue closed by authorities");
        detail.Refunds.Should().ContainSingle().Which.ExpectedAmount.Should().Be(150);
        detail.History[^1].ChangedBy.Should().Be(_admin.ToString("D"));
        _recorder.Audits.Should().ContainSingle(a => a.Action == "admin.booking.force_cancelled" && a.ActorId == _admin);
        (await service.ForceCancelAsync(Guid.NewGuid(), "x", _admin)).Error.Should().Be(BookingErrors.NotFound);
    }

    private AdminVenueService Venues(VenuesDbContext db) => new(db, new NoStats(), _accounts, _recorder, _recorder, _clock);

    private AdminBookingService Bookings(BookingDbContext db) =>
        new(db, new VenueCancelBookingHandler(db, _clock, new NoEffects(), new NoEffects(), NullLogger<VenueCancelBookingHandler>.Instance), _recorder);

    private async Task<Venue> ApprovedVenueAsync(VenuesDbContext db, bool published)
    {
        var venue = Venue.Create(Guid.NewGuid(), "Zamalek Arena", "zamalek-arena", null, _owner, "Addr", "Cairo", "Zamalek", null, null, "+201000000000", null, null, _now);
        venue.SubmitForApproval(_owner, null, _now).IsSuccess.Should().BeTrue();
        venue.Approve(_admin, null, _now).IsSuccess.Should().BeTrue();
        if (published)
        {
            venue.SetPublished(true, _now);
        }

        db.Venues.Add(venue);
        await db.SaveChangesAsync();
        return venue;
    }

    private BookingEntity Online(string reference, Guid venueId, DateTime start, decimal deposit = 100) =>
        BookingEntity.Create(Guid.NewGuid(), reference, venueId, Guid.NewGuid(), _player, start, start.AddHours(1), Money.From(400), Money.From(deposit),
            _now.AddMinutes(30), null, null, _now.AddDays(-1), "p").Value;

    private static VenuesDbContext VenuesDb() => new(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static BookingDbContext BookingDb() => new(new DbContextOptionsBuilder<BookingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class NoStats : IVenueBookingStatsReader
    {
        public Task<VenueBookingStats> GetAsync(Guid venueId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(new VenueBookingStats(0, 0, 0, 0, 0, 0, 0, "EGP", null));
    }

    private sealed class Accounts : IUserAccountsContract
    {
        public HashSet<Guid> Known { get; } = [];
        public List<(Guid UserId, string Role)> Granted { get; } = [];

        public string? NormalizePhone(string phoneNumber) => phoneNumber;

        public Task<bool> MatchesContactAsync(Guid userId, string? email, string? normalizedPhone, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.Where(Known.Contains).ToDictionary(id => id, _ => "Owner"));

        public Task GrantRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default)
        {
            Granted.Add((userId, role));
            return Task.CompletedTask;
        }

        public Task SendStaffInvitationAsync(string? email, string? normalizedPhone, string venueName, string role, string rawToken,
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

    private sealed class NoEffects : IAvailabilityInvalidator, IBookingRealtimeBroadcaster
    {
        public Task InvalidatePitchAsync(Guid pitchId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task InvalidateVenueAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BroadcastSlotStatusChangedAsync(Guid venueId, Guid pitchId, DateTime startUtc, DateTime endUtc, string status, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
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
