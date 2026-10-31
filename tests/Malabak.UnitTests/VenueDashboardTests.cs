using FluentAssertions;
using Malabak.Modules.Booking.Application.Dashboard;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;
using BookingEntity = Malabak.Modules.Booking.Domain.Booking;

namespace Malabak.UnitTests;

public sealed class VenueDashboardTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    // Thursday 2026-11-05, 12:00 in Cairo.
    private static readonly DateTime _now = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly _today = new(2026, 11, 5);

    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();
    private readonly Guid _player = Guid.NewGuid();
    private readonly FakeCapacity _capacity = new();
    private BookingEntity _confirmedToday = default!;
    private BookingEntity _inReviewToday = default!;
    private BookingEntity _manualToday = default!;
    private BookingEntity _liveHoldToday = default!;

    private static DateTime Local(int day, int hour) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, day, 0, 0, 0, DateTimeKind.Unspecified).AddHours(hour), _cairo);

    [Fact]
    public async Task TodayCounts_IgnoreLapsedHoldsAndOtherVenuesAsync()
    {
        await using var db = await SeedAsync();

        var counts = (await Handler(db).HandleAsync(new GetVenueDashboardQuery(_venueId, null, true))).Value.Today;

        counts.TodayBookings.Should().Be(4, "confirmed, in review, manual and the live hold; the lapsed hold and the cancellation do not count");
        counts.Confirmed.Should().Be(2);
        counts.PendingDeposit.Should().Be(1);
        counts.PendingProofReview.Should().Be(1);
        counts.Cancelled.Should().Be(1);
        counts.NoShows.Should().Be(0);
    }

    [Fact]
    public async Task Revenue_SeparatesCollectedDepositsFromPendingOnesAsync()
    {
        await using var db = await SeedAsync();

        var dashboard = (await Handler(db).HandleAsync(new GetVenueDashboardQuery(_venueId, null, true))).Value;

        dashboard.TodayRevenue.Should().Be(new DashboardRevenue("EGP", ConfirmedValue: 700, DepositsCollected: 120, DueAtVenue: 580, DepositsPendingReview: 120, ExpectedValue: 1500));

        // The month adds a completed booking (500 / 150 deposit) and a no-show whose 100 deposit was kept.
        dashboard.MonthRevenue.Should().Be(new DashboardRevenue("EGP", ConfirmedValue: 1200, DepositsCollected: 370, DueAtVenue: 930, DepositsPendingReview: 120, ExpectedValue: 2000));
        dashboard.RevenueTrend.Should().HaveCount(30);
        dashboard.RevenueTrend![^1].Should().Be(new RevenuePoint(_today, 700, 120));
    }

    [Fact]
    public async Task StaffWithoutReportsPermission_GetNoMoneyFiguresAsync()
    {
        await using var db = await SeedAsync();

        var dashboard = (await Handler(db).HandleAsync(new GetVenueDashboardQuery(_venueId, null, IncludeRevenue: false))).Value;

        dashboard.TodayRevenue.Should().BeNull();
        dashboard.MonthRevenue.Should().BeNull();
        dashboard.RevenueTrend.Should().BeNull();
        dashboard.Today.TodayBookings.Should().Be(4);
    }

    [Fact]
    public async Task Occupancy_IsBookedOverOpenMinutes_ForTodayAndTwoWeeksAsync()
    {
        await using var db = await SeedAsync();
        _capacity.Minutes[_today] = 600;

        var dashboard = (await Handler(db).HandleAsync(new GetVenueDashboardQuery(_venueId, null, true))).Value;

        dashboard.TodayOccupancy.Should().Be(new OccupancyPoint(_today, 600, 240, 40m));
        dashboard.OccupancyThisWeek.Select(p => p.Date).Should().Equal(Enumerable.Range(0, 7).Select(i => _today.AddDays(i - 6)));
        dashboard.OccupancyLastWeek.Should().HaveCount(7).And.OnlyContain(p => p.Date < _today.AddDays(-6));
        dashboard.OccupancyThisWeek[0].Percent.Should().Be(0, "a closed day is 0%, not a division by zero");
    }

    [Fact]
    public async Task Timeline_ShowsNamesAndDepositStates_AndProofsAwaitingReviewAsync()
    {
        await using var db = await SeedAsync();

        var dashboard = (await Handler(db).HandleAsync(new GetVenueDashboardQuery(_venueId, null, true))).Value;

        dashboard.Timeline.Select(t => (t.BookingId, t.CustomerName, t.DepositState)).Should().Equal(
            (_confirmedToday.Id, "Karim Adel", "Approved"),
            (_inReviewToday.Id, "Karim Adel", "UnderReview"),
            (_manualToday.Id, "Fathi", "PayAtVenue"),
            (_liveHoldToday.Id, "Karim Adel", "AwaitingUpload"));
        dashboard.ProofsAwaitingReview.Should().ContainSingle().Which.BookingId.Should().Be(_inReviewToday.Id);
        dashboard.Upcoming.Should().NotContain(b => b.Status == BookingStatus.Cancelled);
        dashboard.RecentActivity.Should().NotBeEmpty().And.OnlyContain(a => a.Reference.StartsWith("MLB-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FarAwayDates_AreRejectedAsync()
    {
        await using var db = await SeedAsync();

        (await Handler(db).HandleAsync(new GetVenueDashboardQuery(_venueId, _today.AddYears(3), true))).Error.Should().Be(VenueDashboardErrors.DateOutOfRange);
    }

    [Fact]
    public void BusinessDayStart_SurvivesTheMidnightDstJump()
    {
        // 2026-04-24: Cairo clocks jump from 00:00 to 01:00, so local midnight does not exist.
        var start = GetVenueDashboardHandler.ToUtc(new DateOnly(2026, 4, 24), _cairo);

        start.Should().Be(new DateTime(2026, 4, 23, 22, 0, 0, DateTimeKind.Utc));
    }

    private GetVenueDashboardHandler Handler(BookingDbContext db) => new(db, _capacity, new FakeUsers(_player), new FixedClock());

    private async Task<BookingDbContext> SeedAsync()
    {
        var db = new BookingDbContext(new DbContextOptionsBuilder<BookingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var created = _now.AddDays(-5);

        _confirmedToday = Online(19, 400, 120, created);
        _confirmedToday.SubmitProof(Guid.NewGuid(), "player", created).IsSuccess.Should().BeTrue();
        _confirmedToday.Confirm("staff", created).IsSuccess.Should().BeTrue();

        _inReviewToday = Online(20, 400, 120, created);
        _inReviewToday.SubmitProof(Guid.NewGuid(), "player", created).IsSuccess.Should().BeTrue();

        _manualToday = BookingEntity.CreateManual(Guid.NewGuid(), "MLB-M1", _venueId, _pitchId, Guid.NewGuid(), "Fathi", "+201000000000",
            Local(5, 21), Local(5, 22), Money.From(300), Money.From(0), true, null, created).Value;

        var lapsed = Online(22, 400, 120, _now.AddMinutes(-20), holdMinutes: 15);
        _liveHoldToday = Online(23, 400, 120, _now.AddMinutes(-5), holdMinutes: 15);

        var cancelled = Online(18, 400, 120, created);
        cancelled.Cancel("changed plans", "player", isStaff: false, created).IsSuccess.Should().BeTrue();

        var completed = BookingEntity.Create(Guid.NewGuid(), "MLB-C1", _venueId, _pitchId, _player, Local(2, 19), Local(2, 20),
            Money.From(500), Money.From(150), _now.AddDays(-4), null, null, _now.AddDays(-4).AddMinutes(-15), "player").Value;
        completed.SubmitProof(Guid.NewGuid(), "player", _now.AddDays(-4).AddMinutes(-10)).IsSuccess.Should().BeTrue();
        completed.Confirm("staff", _now.AddDays(-4).AddMinutes(-5)).IsSuccess.Should().BeTrue();
        completed.Complete(Local(2, 21)).IsSuccess.Should().BeTrue();

        var noShow = BookingEntity.Create(Guid.NewGuid(), "MLB-N1", _venueId, _pitchId, _player, Local(4, 19), Local(4, 20),
            Money.From(400), Money.From(100), _now.AddDays(-3), null, null, _now.AddDays(-3).AddMinutes(-15), "player").Value;
        noShow.SubmitProof(Guid.NewGuid(), "player", _now.AddDays(-3).AddMinutes(-10)).IsSuccess.Should().BeTrue();
        noShow.Confirm("staff", _now.AddDays(-3).AddMinutes(-5)).IsSuccess.Should().BeTrue();
        noShow.MarkNoShow("did not come", "staff", Local(4, 20)).IsSuccess.Should().BeTrue();

        var otherVenue = BookingEntity.CreateManual(Guid.NewGuid(), "XXX-1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Other", "+201000000001",
            Local(5, 19), Local(5, 20), Money.From(999), Money.From(0), true, null, created).Value;

        db.Bookings.AddRange(_confirmedToday, _inReviewToday, _manualToday, lapsed, _liveHoldToday, cancelled, completed, noShow, otherVenue);
        await db.SaveChangesAsync();
        return db;
    }

    private BookingEntity Online(int hour, decimal total, decimal deposit, DateTime createdUtc, int holdMinutes = 60 * 24 * 10) =>
        BookingEntity.Create(Guid.NewGuid(), $"MLB-{Guid.NewGuid():N}"[..12], _venueId, _pitchId, _player, Local(5, hour), Local(5, hour + 1),
            Money.From(total), Money.From(deposit), createdUtc.AddMinutes(holdMinutes), null, null, createdUtc, "player").Value;

    private sealed class FakeCapacity : IVenueCapacityReader
    {
        public Dictionary<DateOnly, int> Minutes { get; } = [];

        public Task<IReadOnlyDictionary<DateOnly, int>> GetOpenMinutesAsync(
            Guid venueId, DateOnly from, DateOnly to, Guid? pitchId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<DateOnly, int>>(Minutes.Where(m => m.Key >= from && m.Key <= to).ToDictionary());

        public Task<IReadOnlyList<PitchOpenMinutes>> GetOpenMinutesByPitchAsync(
            Guid venueId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PitchOpenMinutes>>([]);
    }

    private sealed class FakeUsers(Guid player) : IUserAccountsContract
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
        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(_now, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
