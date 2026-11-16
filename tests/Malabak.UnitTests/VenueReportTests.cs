using System.Text;
using FluentAssertions;
using Malabak.Modules.Booking.Application.Reports;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;
using BookingEntity = Malabak.Modules.Booking.Domain.Booking;

namespace Malabak.UnitTests;

public sealed class VenueReportTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    private static readonly DateTime _now = new(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly _from = new(2026, 11, 1);
    private static readonly DateOnly _to = new(2026, 11, 30);

    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _pitchA = Guid.NewGuid();
    private readonly Guid _pitchB = Guid.NewGuid();
    private readonly Guid _player = Guid.NewGuid();

    [Fact]
    public async Task Revenue_ClassifiesBookedReceivedOutstandingRecognizedAndPendingAsync()
    {
        await using var db = await SeedAsync();

        var report = (await Service(db).GetAsync(_venueId, _from, _to, null, ReportGrouping.Month)).Value;

        // Accepted: completed 400/100, confirmed future 500/150, no-show 400/100. Waiting: in review 300. Lapsed hold, cancellation: none.
        report.Revenue.Should().Be(new RevenueSplit("EGP", Booked: 1300, DepositsReceived: 350, Outstanding: 350, Recognized: 500, Pending: 300, PlatformCommission: 0, VenueNetRevenue: 1300));
        report.RevenueSeries.Should().ContainSingle().Which.Should().Be(new RevenueBucket(_from, 1300, 500, 350));
    }

    [Fact]
    public async Task StatusDistribution_TreatsLapsedHoldsAsExpired_AndIgnoresOtherVenuesAsync()
    {
        await using var db = await SeedAsync();

        var report = (await Service(db).GetAsync(_venueId, _from, _to, null, ReportGrouping.Month)).Value;

        report.StatusDistribution.Should().Equal(new Dictionary<string, int>
        {
            ["Cancelled"] = 1,
            ["Completed"] = 1,
            ["Confirmed"] = 1,
            ["Expired"] = 1,
            ["NoShow"] = 1,
            ["ProofSubmitted"] = 1
        });
    }

    [Fact]
    public async Task Weeks_StartOnSaturday_AndEveryPeriodIsPresentEvenWhenEmptyAsync()
    {
        await using var db = await SeedAsync();

        var weeks = (await Service(db).GetAsync(_venueId, _from, _to, null, ReportGrouping.Week)).Value.Bookings;

        weeks.Select(w => w.PeriodStart).Should().Equal(
            new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 28));
        weeks.Should().OnlyContain(w => w.PeriodStart.DayOfWeek == DayOfWeek.Saturday);
        weeks.Sum(w => w.Total).Should().Be(6);
        weeks[3].Total.Should().Be(0);

        var days = (await Service(db).GetAsync(_venueId, _from, _to, null, ReportGrouping.Day)).Value.Bookings;
        days.Should().HaveCount(30);
    }

    [Fact]
    public async Task PitchFilter_Utilization_AndPeakHoursAsync()
    {
        await using var db = await SeedAsync();

        var report = (await Service(db).GetAsync(_venueId, _from, _to, _pitchB, ReportGrouping.Month)).Value;

        report.StatusDistribution.Values.Sum().Should().Be(1, "only the pitch B booking");
        report.PitchUtilization.Should().ContainSingle().Which.Should().Be(new PitchUtilization(_pitchB, "B", 1200, 60, 5m));

        var all = (await Service(db).GetAsync(_venueId, _from, _to, null, ReportGrouping.Month)).Value;
        all.PeakHours.Should().Equal(
            new PeakHourCell((int)DayOfWeek.Monday, 19, 4),
            new PeakHourCell((int)DayOfWeek.Monday, 20, 1));
        all.PeakHours.Sum(c => c.Bookings).Should().Be(5, "completed, confirmed, no-show, in review and the 2-hour booking's second hour; cancelled and expired don't count");
    }

    [Fact]
    public async Task Csv_HasBomHeaderAndRows_AndNeutralizesFormulasAsync()
    {
        await using var db = await SeedAsync();

        var bytes = (await Service(db).ExportCsvAsync(_venueId, _from, _to, null)).Value;

        bytes.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().StartWith("Reference,Date,Start,End");
        lines.Should().HaveCount(7, "header plus the six bookings of this venue");
        lines.Should().Contain(l => l.Contains("\"'=HYPERLINK(\"\"x\"\")\"", StringComparison.Ordinal), "a formula-like name is quoted and prefixed");
        lines.Should().Contain(l => l.StartsWith("R-EXP", StringComparison.Ordinal) && l.Contains(",Expired,", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("+20100", "\"'+20100\"")]
    [InlineData("-5", "\"'-5\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("Karim, Adel", "\"Karim, Adel\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("plain", "plain")]
    public void CsvCells_AreEscaped(string value, string expected)
    {
        VenueReportService.Cell(value).Should().Be(expected);
    }

    [Fact]
    public async Task InvalidRanges_AreRejectedAsync()
    {
        await using var db = await SeedAsync();
        var service = Service(db);

        (await service.GetAsync(_venueId, _to, _from, null, ReportGrouping.Day)).Error.Should().Be(ReportErrors.InvalidRange);
        (await service.GetAsync(_venueId, _from, _from.AddDays(366), null, ReportGrouping.Day)).Error.Should().Be(ReportErrors.InvalidRange);
        (await service.ExportCsvAsync(_venueId, _to, _from, null)).Error.Should().Be(ReportErrors.InvalidRange);
    }

    private VenueReportService Service(BookingDbContext db) => new(db, new FakeCapacity(_pitchA, _pitchB), new FakeUsers(), new FixedClock());

    private static DateTime Local(int day, int hour) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, day, 0, 0, 0, DateTimeKind.Unspecified).AddHours(hour), _cairo);

    private async Task<BookingDbContext> SeedAsync()
    {
        var db = new BookingDbContext(new DbContextOptionsBuilder<BookingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var created = new DateTime(2026, 10, 25, 10, 0, 0, DateTimeKind.Utc);

        // Monday 2 Nov 19:00 (pitch A): completed.
        var completed = Online("R-COMP", _pitchA, Local(2, 19), 1, 400, 100, created);
        Confirm(completed, created);
        completed.Complete(Local(2, 21)).IsSuccess.Should().BeTrue();

        // Monday 9 Nov 19:00-21:00 (pitch A): no-show, deposit kept.
        var noShow = Online("R-NS", _pitchA, Local(9, 19), 2, 400, 100, created);
        Confirm(noShow, created);
        noShow.MarkNoShow("absent", "s", Local(9, 19)).IsSuccess.Should().BeTrue();

        // Monday 16 Nov 19:00 (pitch B): confirmed, not played yet.
        var confirmed = Online("R-CONF", _pitchB, Local(16, 19), 1, 500, 150, created);
        Confirm(confirmed, created);

        // Monday 2 Nov 19:00 (pitch A... overlapping is fine in memory): in review. Name tries formula injection.
        var inReview = BookingEntity.CreateManual(Guid.NewGuid(), "R-REV", _venueId, _pitchA, Guid.NewGuid(), "=HYPERLINK(\"x\")", "+201000000000",
            Local(2, 19), Local(2, 20), Money.From(300), Money.From(90), false, created.AddDays(60), created).Value;
        inReview.SubmitProof(Guid.NewGuid(), "p", created).IsSuccess.Should().BeTrue();

        // Lapsed hold on 3 Nov and a cancellation on 4 Nov.
        var lapsed = Online("R-EXP", _pitchA, Local(3, 19), 1, 400, 100, created, holdUntil: created.AddMinutes(15));
        var cancelled = Online("R-CAN", _pitchA, Local(4, 19), 1, 400, 100, created);
        cancelled.Cancel("busy", "p", false, created).IsSuccess.Should().BeTrue();

        var otherVenue = BookingEntity.CreateManual(Guid.NewGuid(), "OTHER", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Other", "+201000000001",
            Local(2, 19), Local(2, 20), Money.From(999), Money.From(0), true, null, created).Value;

        db.Bookings.AddRange(completed, noShow, confirmed, inReview, lapsed, cancelled, otherVenue);
        await db.SaveChangesAsync();
        return db;
    }

    private BookingEntity Online(string reference, Guid pitch, DateTime start, int hours, decimal total, decimal deposit, DateTime created, DateTime? holdUntil = null) =>
        BookingEntity.Create(Guid.NewGuid(), reference, _venueId, pitch, _player, start, start.AddHours(hours), Money.From(total), Money.From(deposit),
            holdUntil ?? created.AddDays(60), null, null, created, "p").Value;

    private static void Confirm(BookingEntity booking, DateTime at)
    {
        booking.SubmitProof(Guid.NewGuid(), "p", at).IsSuccess.Should().BeTrue();
        booking.Confirm("s", at).IsSuccess.Should().BeTrue();
    }

    private sealed class FakeCapacity(Guid pitchA, Guid pitchB) : IVenueCapacityReader
    {
        public Task<IReadOnlyDictionary<DateOnly, int>> GetOpenMinutesAsync(
            Guid venueId, DateOnly from, DateOnly to, Guid? pitchId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<DateOnly, int>>(new Dictionary<DateOnly, int>());

        public Task<IReadOnlyList<PitchOpenMinutes>> GetOpenMinutesByPitchAsync(
            Guid venueId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PitchOpenMinutes>>([new PitchOpenMinutes(pitchA, "A", 1200), new PitchOpenMinutes(pitchB, "B", 1200)]);
    }

    private sealed class FakeUsers : IUserAccountsContract
    {
        public string? NormalizePhone(string phoneNumber) => phoneNumber;

        public Task<bool> MatchesContactAsync(Guid userId, string? email, string? normalizedPhone, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.ToDictionary(id => id, _ => "Karim Adel"));

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
