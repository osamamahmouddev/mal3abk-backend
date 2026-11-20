using FluentAssertions;
using Malabak.Modules.Booking.Application.Admin;
using Malabak.Modules.Booking.Application.Bookings.CancelBooking;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using BookingEntity = Malabak.Modules.Booking.Domain.Booking;

namespace Malabak.UnitTests;

/// <summary>
/// The commission settlement cycle: the platform team confirms a deposit paid to the platform, keeps the commission fixed in the
/// booking's quote, owes the venue the rest, and records the transfers it makes.
/// </summary>
public sealed class VenuePayoutTests
{
    private static readonly DateTime _now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Recorder _recorder = new();
    private readonly Directory _directory;

    public VenuePayoutTests()
    {
        _directory = new Directory(_venueId, _ownerId);
    }

    [Fact]
    public async Task ApprovingADeposit_RecordsItAsHeldByThePlatformAsync()
    {
        await using var db = BookingDb();
        var booking = await ProofSubmittedAsync(db, "MAL-P1", deposit: 100, commission: 40);

        var result = await Bookings(db).ReviewDepositAsync(booking.Id, approved: true, rejectionReason: null, _admin);

        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Confirmed);
        booking.IsCommissionPaid.Should().BeTrue();
        booking.OnlinePaidAmount.Amount.Should().Be(100);
    }

    [Fact]
    public async Task Balance_IsTheDepositsLessCommission_OnlyForConfirmedAppBookingsAsync()
    {
        await using var db = BookingDb();
        var confirmed = await ProofSubmittedAsync(db, "MAL-P2", deposit: 100, commission: 40);
        await ProofSubmittedAsync(db, "MAL-P3", deposit: 100, commission: 40); // still waiting for review: not paid yet
        db.Bookings.Add(Manual("MAL-M1"));
        await db.SaveChangesAsync();
        (await Bookings(db).ReviewDepositAsync(confirmed.Id, true, null, _admin)).IsSuccess.Should().BeTrue();

        var balances = await Payouts(db).GetBalancesAsync();

        var balance = balances.Should().ContainSingle().Subject;
        balance.SettledBookings.Should().Be(1);
        balance.DepositsReceived.Should().Be(100);
        balance.Commission.Should().Be(40);
        balance.VenueShare.Should().Be(60);
        balance.Balance.Should().Be(60);
    }

    [Fact]
    public async Task CancelledBooking_RefundedToThePlayer_OwesTheVenueNothingAsync()
    {
        await using var db = BookingDb();
        var booking = await ProofSubmittedAsync(db, "MAL-P4", deposit: 100, commission: 40);
        (await Bookings(db).ReviewDepositAsync(booking.Id, true, null, _admin)).IsSuccess.Should().BeTrue();
        booking.Cancel("Rain", "staff", isStaff: true, _now).IsSuccess.Should().BeTrue();
        db.BookingRefunds.Add(BookingRefund.CreatePending(Guid.NewGuid(), booking.Id, 100, "EGP", "Rain", null, _now));
        await db.SaveChangesAsync();

        var statement = await Payouts(db).GetStatementAsync(_venueId);

        statement.Value.Balance.RefundedToPlayers.Should().Be(100);
        statement.Value.Balance.Commission.Should().Be(0);
        statement.Value.Balance.Balance.Should().Be(0);
    }

    [Fact]
    public async Task CancelledBooking_PartlyRefunded_KeepsTheCommissionFirstAsync()
    {
        await using var db = BookingDb();
        var booking = await ProofSubmittedAsync(db, "MAL-P5", deposit: 100, commission: 40);
        (await Bookings(db).ReviewDepositAsync(booking.Id, true, null, _admin)).IsSuccess.Should().BeTrue();
        booking.Cancel("Late", "player", isStaff: false, _now).IsSuccess.Should().BeTrue();
        db.BookingRefunds.Add(BookingRefund.CreatePending(Guid.NewGuid(), booking.Id, 50, "EGP", "Late", null, _now));
        await db.SaveChangesAsync();

        var statement = await Payouts(db).GetStatementAsync(_venueId);

        // 100 received, 50 owed back to the player: the platform keeps its 40 out of the 50 left, the venue gets 10.
        statement.Value.Bookings.Should().ContainSingle().Which.VenueShare.Should().Be(10);
        statement.Value.Balance.Balance.Should().Be(10);
    }

    [Fact]
    public async Task RecordPayout_ReducesTheBalance_AuditsAndTellsTheVenueAsync()
    {
        await using var db = BookingDb();
        var booking = await ProofSubmittedAsync(db, "MAL-P6", deposit: 100, commission: 40);
        (await Bookings(db).ReviewDepositAsync(booking.Id, true, null, _admin)).IsSuccess.Should().BeTrue();
        var payouts = Payouts(db);

        var tooMuch = await payouts.RecordAsync(_venueId, new RecordVenuePayoutInput(61, PayoutMethods.InstaPay, "IP-1", null, null), _admin);
        var paid = await payouts.RecordAsync(_venueId, new RecordVenuePayoutInput(60, PayoutMethods.InstaPay, "IP-2", "Week 47", _now.AddHours(-1)), _admin);

        tooMuch.Error.Should().Be(VenuePayoutErrors.ExceedsBalance);
        paid.IsSuccess.Should().BeTrue();
        (await payouts.GetStatementAsync(_venueId)).Value.Balance.Balance.Should().Be(0);
        _recorder.Audits.Should().Contain(a => a.Action == "admin.venue.payout_recorded" && a.ActorId == _admin);
        _recorder.Notifications.Should().ContainSingle(n => n.UserId == _ownerId && n.Type == NotificationTypes.VenuePayoutRecorded);
    }

    [Theory]
    [InlineData(0, "InstaPay", "IP-1", "Payout.InvalidAmount")]
    [InlineData(10.123, "InstaPay", "IP-1", "Payout.InvalidAmount")]
    [InlineData(10, "Bitcoin", "IP-1", "Payout.InvalidMethod")]
    [InlineData(10, "Wallet", "  ", "Payout.ReferenceRequired")]
    public async Task RecordPayout_RejectsInvalidInputAsync(double amount, string method, string reference, string code)
    {
        await using var db = BookingDb();

        var result = await Payouts(db).RecordAsync(_venueId, new RecordVenuePayoutInput((decimal)amount, method, reference, null, null), _admin);

        result.Error.Code.Should().Be(code);
    }

    [Fact]
    public async Task RecordPayout_InTheFuture_IsRejectedAsync()
    {
        await using var db = BookingDb();

        var result = await Payouts(db).RecordAsync(_venueId, new RecordVenuePayoutInput(10, "Cash", "C-1", null, _now.AddDays(1)), _admin);

        result.Error.Should().Be(VenuePayoutErrors.InvalidPaidAt);
    }

    [Fact]
    public async Task UnknownVenue_IsNotFoundAsync()
    {
        await using var db = BookingDb();

        (await Payouts(db).GetStatementAsync(Guid.NewGuid())).Error.Should().Be(VenuePayoutErrors.VenueNotFound);
    }

    [Fact]
    public async Task FinancialSummary_UsesTheCommissionFixedAtBooking_NotTheVenuesTermsTodayAsync()
    {
        await using var db = BookingDb();
        var booking = await ProofSubmittedAsync(db, "MAL-P7", deposit: 100, commission: 40);
        (await Bookings(db).ReviewDepositAsync(booking.Id, true, null, _admin)).IsSuccess.Should().BeTrue();
        _directory.Commission = new VenueCommissionInfo(_venueId, true, "Percentage", 50m);

        var summary = await Bookings(db).GetVenuesFinancialSummaryAsync(null, null);

        var row = summary.Value.Should().ContainSingle().Subject;
        row.TotalPlatformCommission.Should().Be(40);
        row.PlatformCollectedOnline.Should().Be(100);
        row.PlatformOwesVenue.Should().Be(60);
    }

    private async Task<BookingEntity> ProofSubmittedAsync(BookingDbContext db, string reference, decimal deposit, decimal commission)
    {
        var start = _now.AddDays(1);
        var quote = new PriceQuoteSnapshot(_venueId, Guid.NewGuid(), start, start.AddHours(1), 60, "EGP", 400, 0, null, 400, deposit,
            "Percentage", [], _now, PlatformCommission: commission);
        var booking = BookingEntity.Create(Guid.NewGuid(), reference, _venueId, quote.PitchId, Guid.NewGuid(), start, start.AddHours(1),
            Money.From(400), Money.From(deposit), _now.AddMinutes(30), quote, null, _now, "player").Value;
        booking.SubmitProof(Guid.NewGuid(), "player", _now).IsSuccess.Should().BeTrue();
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking;
    }

    private BookingEntity Manual(string reference)
    {
        var start = _now.AddDays(2);
        return BookingEntity.CreateManual(Guid.NewGuid(), reference, _venueId, Guid.NewGuid(), _ownerId, "Walk-in", "+201000000001",
            start, start.AddHours(1), Money.From(400), Money.From(100), confirmedImmediately: true, null, _now).Value;
    }

    private AdminBookingService Bookings(BookingDbContext db) =>
        new(db, new VenueCancelBookingHandler(db, new FixedClock(), new NoEffects(), new NoEffects(), NullLogger<VenueCancelBookingHandler>.Instance),
            _recorder, new FixedClock(), new NoEffects(), new NoEffects(), _directory);

    private VenuePayoutService Payouts(BookingDbContext db) => new(db, _recorder, new FixedClock(), _directory, _recorder);

    private static BookingDbContext BookingDb() =>
        new(new DbContextOptionsBuilder<BookingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Directory(Guid knownVenueId, Guid ownerId) : IVenueDirectory
    {
        public VenueCommissionInfo? Commission { get; set; }

        public Task<Guid?> GetVenueIdOfPitchAsync(Guid pitchId, CancellationToken cancellationToken = default) => Task.FromResult<Guid?>(knownVenueId);

        public Task<VenueSummary?> GetVenueSummaryAsync(Guid venueId, CancellationToken cancellationToken = default) =>
            Task.FromResult<VenueSummary?>(new VenueSummary(venueId, "Arena", "Addr", null, null));

        public Task<IReadOnlyList<VenueBlockedPeriod>> GetBlockedPeriodsAsync(Guid venueId, DateTime fromUtc, DateTime toUtc, Guid? pitchId,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<VenueBlockedPeriod>>([]);

        public Task<IReadOnlyDictionary<Guid, string>> GetVenueNamesAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(venueIds.Where(id => id == knownVenueId).ToDictionary(id => id, _ => "Arena"));

        public Task<IReadOnlyDictionary<Guid, string>> GetVenueCoverPhotosAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyDictionary<Guid, string>> GetPitchNamesAsync(IReadOnlyCollection<Guid> pitchIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyDictionary<Guid, VenueCommissionInfo>> GetVenueCommissionsAsync(IReadOnlyCollection<Guid> venueIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, VenueCommissionInfo>>(
                Commission is null ? new Dictionary<Guid, VenueCommissionInfo>() : new Dictionary<Guid, VenueCommissionInfo> { [knownVenueId] = Commission });

        public Task<IReadOnlyList<Guid>> GetStaffUserIdsAsync(Guid venueId, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(venueId == knownVenueId ? [ownerId] : []);
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

    private sealed class NoEffects : IAvailabilityInvalidator, Malabak.Modules.Booking.Application.IBookingRealtimeBroadcaster
    {
        public Task InvalidatePitchAsync(Guid pitchId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task InvalidateVenueAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BroadcastSlotStatusChangedAsync(Guid venueId, Guid pitchId, DateTime startUtc, DateTime endUtc, string status,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
