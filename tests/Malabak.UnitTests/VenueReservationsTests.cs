using FluentAssertions;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Bookings.CancelBooking;
using Malabak.Modules.Booking.Application.Bookings.VenueQueries;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using BookingEntity = Malabak.Modules.Booking.Domain.Booking;

namespace Malabak.UnitTests;

public sealed class VenueReservationsTests
{
    private static readonly DateTime _now = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();
    private readonly Guid _player = Guid.NewGuid();
    private readonly Guid _staff = Guid.NewGuid();
    private readonly RecordingSideEffects _effects = new();

    [Fact]
    public async Task StaffCancel_FreesTheSlot_AndOwesTheFullDepositBackAsync()
    {
        await using var db = NewDb();
        var booking = Confirmed("C-1", _now.AddHours(2), deposit: 100);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var result = await Cancel(db).HandleAsync(new VenueCancelBookingCommand(booking.Id, _venueId, _staff, "  Pitch maintenance  "));

        result.IsSuccess.Should().BeTrue("staff may cancel even two hours before the start, unlike players");
        var saved = await db.Bookings.SingleAsync();
        saved.Status.Should().Be(BookingStatus.Cancelled);
        saved.CancellationReason.Should().Be("Pitch maintenance");
        (await db.BookingRefunds.SingleAsync()).ExpectedRefundAmount.Should().Be(100, "the venue cancelled, so the whole deposit is owed back");
        _effects.Invalidated.Should().Contain(_pitchId);
        _effects.SlotStatuses.Should().Contain("free");
    }

    [Fact]
    public async Task StaffCancel_RequiresAReason_AndCannotTouchOtherVenuesOrFinishedBookingsAsync()
    {
        await using var db = NewDb();
        var booking = Confirmed("C-2", _now.AddDays(1), deposit: 100);
        var otherVenue = BookingEntity.CreateManual(Guid.NewGuid(), "X-1", Guid.NewGuid(), Guid.NewGuid(), _staff, "Other", "+201000000001",
            _now.AddDays(1), _now.AddDays(1).AddHours(1), Money.From(300), Money.From(0), true, null, _now.AddDays(-1)).Value;
        db.Bookings.AddRange(booking, otherVenue);
        await db.SaveChangesAsync();
        var handler = Cancel(db);

        (await handler.HandleAsync(new VenueCancelBookingCommand(booking.Id, _venueId, _staff, "   "))).Error.Should().Be(BookingErrors.ReasonRequired);
        (await handler.HandleAsync(new VenueCancelBookingCommand(otherVenue.Id, _venueId, _staff, "nope"))).Error.Should().Be(BookingErrors.NotFound);
        (await handler.HandleAsync(new VenueCancelBookingCommand(booking.Id, _venueId, _staff, new string('x', 501)))).IsFailure.Should().BeTrue();

        (await handler.HandleAsync(new VenueCancelBookingCommand(booking.Id, _venueId, _staff, "duplicate"))).IsSuccess.Should().BeTrue();
        (await handler.HandleAsync(new VenueCancelBookingCommand(booking.Id, _venueId, _staff, "again"))).Error.Should().Be(BookingErrors.InvalidTransition);
    }

    [Fact]
    public async Task ManualBookings_AreCancelledWithoutARefundRecordAsync()
    {
        await using var db = NewDb();
        var manual = BookingEntity.CreateManual(Guid.NewGuid(), "M-1", _venueId, _pitchId, _staff, "Fathi", "+201000000002",
            _now.AddDays(1), _now.AddDays(1).AddHours(1), Money.From(300), Money.From(0), true, null, _now.AddDays(-1)).Value;
        db.Bookings.Add(manual);
        await db.SaveChangesAsync();

        (await Cancel(db).HandleAsync(new VenueCancelBookingCommand(manual.Id, _venueId, _staff, "rain"))).IsSuccess.Should().BeTrue();

        (await db.BookingRefunds.CountAsync()).Should().Be(0, "nothing was paid through the app");
    }

    [Fact]
    public async Task List_FiltersBySeveralStatuses_SortsByWhitelistedKeys_AndShowsPlayerNamesAsync()
    {
        await using var db = NewDb();
        var confirmed = Confirmed("C-3", _now.AddDays(2), deposit: 100, total: 400);
        var inReview = Online("R-1", _now.AddDays(1), total: 600);
        inReview.SubmitProof(Guid.NewGuid(), "p", _now.AddDays(-1)).IsSuccess.Should().BeTrue();
        var waiting = Online("W-1", _now.AddDays(3), total: 300);
        db.Bookings.AddRange(confirmed, inReview, waiting);
        await db.SaveChangesAsync();
        var handler = new VenueBookingQueriesHandler(db, new NoVenues(), new Names(_player));

        var pending = (await handler.HandleAsync(new GetVenueBookingsQuery(_venueId, null, null, null, null, null, 1, 20,
            [BookingStatus.PendingDeposit, BookingStatus.ProofSubmitted], "start_asc"))).Value;

        pending.Items.Select(i => i.Reference).Should().Equal("R-1", "W-1");
        pending.TotalCount.Should().Be(2);
        pending.Items.Should().OnlyContain(i => i.CustomerName == "Karim Adel" && !i.IsManual);

        var byPrice = (await handler.HandleAsync(new GetVenueBookingsQuery(_venueId, null, null, null, null, null, 1, 20, null, "price_desc"))).Value;
        byPrice.Items.Select(i => i.Reference).Should().Equal("R-1", "C-3", "W-1");

        var fallback = (await handler.HandleAsync(new GetVenueBookingsQuery(_venueId, null, null, null, null, null, 1, 20, null, "DROP TABLE"))).Value;
        fallback.Items.Select(i => i.Reference).Should().Equal("W-1", "C-3", "R-1");
    }

    private static BookingDbContext NewDb() => new(new DbContextOptionsBuilder<BookingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private VenueCancelBookingHandler Cancel(BookingDbContext db) =>
        new(db, new FixedClock(), _effects, _effects, NullLogger<VenueCancelBookingHandler>.Instance);

    private BookingEntity Online(string reference, DateTime start, decimal total = 400, decimal deposit = 100) =>
        BookingEntity.Create(Guid.NewGuid(), reference, _venueId, _pitchId, _player, start, start.AddHours(1), Money.From(total), Money.From(deposit),
            _now.AddDays(30), null, null, _now.AddDays(-2), "p").Value;

    private BookingEntity Confirmed(string reference, DateTime start, decimal deposit, decimal total = 400)
    {
        var booking = Online(reference, start, total, deposit);
        booking.SubmitProof(Guid.NewGuid(), "p", _now.AddDays(-2)).IsSuccess.Should().BeTrue();
        booking.Confirm("s", _now.AddDays(-2)).IsSuccess.Should().BeTrue();
        return booking;
    }

    private sealed class RecordingSideEffects : IAvailabilityInvalidator, IBookingRealtimeBroadcaster
    {
        public List<Guid> Invalidated { get; } = [];
        public List<string> SlotStatuses { get; } = [];

        public Task InvalidatePitchAsync(Guid pitchId, CancellationToken cancellationToken = default)
        {
            Invalidated.Add(pitchId);
            return Task.CompletedTask;
        }

        public Task InvalidateVenueAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BroadcastSlotStatusChangedAsync(Guid venueId, Guid pitchId, DateTime startUtc, DateTime endUtc, string status, CancellationToken cancellationToken = default)
        {
            SlotStatuses.Add(status);
            return Task.CompletedTask;
        }

        public Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoVenues : IVenueDirectory
    {
        public Task<Guid?> GetVenueIdOfPitchAsync(Guid pitchId, CancellationToken cancellationToken = default) => Task.FromResult<Guid?>(null);

        public Task<VenueSummary?> GetVenueSummaryAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.FromResult<VenueSummary?>(null);

        public Task<IReadOnlyList<VenueBlockedPeriod>> GetBlockedPeriodsAsync(Guid venueId, DateTime fromUtc, DateTime toUtc, Guid? pitchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VenueBlockedPeriod>>([]);

        public Task<IReadOnlyDictionary<Guid, string>> GetVenueNamesAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyDictionary<Guid, string>> GetPitchNamesAsync(IReadOnlyCollection<Guid> pitchIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyList<Guid>> GetStaffUserIdsAsync(Guid venueId, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    private sealed class Names(Guid player) : IUserAccountsContract
    {
        public string? NormalizePhone(string phoneNumber) => phoneNumber;

        public Task<bool> MatchesContactAsync(Guid userId, string? email, string? normalizedPhone, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.Where(id => id == player).ToDictionary(id => id, _ => "Karim Adel"));

        public Task GrantRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendStaffInvitationAsync(string? email, string? normalizedPhone, string venueName, string role, string rawToken,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(_now, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
