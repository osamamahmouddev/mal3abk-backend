using FluentAssertions;
using Malabak.Api.Endpoints;
using Malabak.Modules.Booking.Application.Admin;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.Modules.Subscriptions.Domain;
using Malabak.Modules.Subscriptions.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;
using BookingEntity = Malabak.Modules.Booking.Domain.Booking;

namespace Malabak.UnitTests;

public sealed class AdminMetricsTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    private static readonly DateTime _now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    private readonly Recorder _recorder = new();
    private readonly Clock _clock = new();

    [Fact]
    public void DailySeries_UsesCairoDays_AndFillsEmptyDaysWithZero()
    {
        // 6 Nov 2026 00:00 Cairo (UTC+2) is 5 Nov 22:00 UTC.
        var period = new MetricsPeriod(new DateTime(2026, 11, 5, 22, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 8, 22, 0, 0, DateTimeKind.Utc), _cairo);

        var series = period.Daily([
            new DateTime(2026, 11, 5, 22, 30, 0, DateTimeKind.Utc), // 6 Nov 00:30 Cairo
            new DateTime(2026, 11, 6, 21, 59, 0, DateTimeKind.Utc), // 6 Nov 23:59 Cairo
            new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc),  // 8 Nov
            new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc)   // outside
        ]);

        series.Select(p => (p.Day, p.Value)).Should().Equal(
            (new DateOnly(2026, 11, 6), 2m), (new DateOnly(2026, 11, 7), 0m), (new DateOnly(2026, 11, 8), 1m));
    }

    [Fact]
    public async Task Mrr_NormalizesAnnualPlans_ExcludesTrials_AndCollectedRevenueSkipsVoidedInvoicesAsync()
    {
        await using var db = new SubscriptionsDbContext(new DbContextOptionsBuilder<SubscriptionsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var admin = Guid.NewGuid();
        var plans = new PlanService(db, _recorder, _clock);
        var plan = (await plans.CreateAsync(new PlanTerms("pro", "Pro", null, 500, 4800, null, null, null, [], 14, 3, true, 0), admin)).Value;
        await plans.SetStatusAsync(plan.Id, PlanStatus.Active, admin);
        var subscriptions = new SubscriptionService(db, new NoUsage(), new InvoiceNumberGenerator(db), _recorder, _recorder, _clock);
        var monthly = Guid.NewGuid();
        var annual = Guid.NewGuid();
        await subscriptions.AssignAsync(monthly, plan.Id, BillingCycle.Monthly, withTrial: false, admin);
        await subscriptions.AssignAsync(annual, plan.Id, BillingCycle.Annual, withTrial: false, admin);
        await subscriptions.AssignAsync(Guid.NewGuid(), plan.Id, BillingCycle.Monthly, withTrial: true, admin);
        await subscriptions.RecordPaymentAsync(monthly, new RecordPaymentInput(500, PaymentMethod.Cash, null, null, null), admin);
        var mistake = (await subscriptions.RecordPaymentAsync(annual, new RecordPaymentInput(4800, PaymentMethod.BankTransfer, null, null, null), admin)).Value;
        await new InvoiceService(db, _recorder, _clock).VoidAsync(mistake.Id, "Recorded on the wrong owner", admin);

        var part = await new SubscriptionMetricsProvider(db).GetAsync(new MetricsPeriod(_now.AddDays(-1), _now.AddDays(1), _cairo));

        part.Figures["subscriptions.active"].Should().Be(2);
        part.Figures["subscriptions.trial"].Should().Be(1);
        part.Figures["mrr"].Should().Be(500 + 400, "annual 4800 counts as 400 a month");
        part.Figures["revenue.collected"].Should().Be(500, "the voided payment is not revenue");
        part.Distributions["subscriptions.by_plan"]["pro"].Should().Be(2);
    }

    [Fact]
    public async Task BookingFigures_CountCreatedAndConfirmedByTheirOwnTimestampsAsync()
    {
        await using var db = new BookingDbContext(new DbContextOptionsBuilder<BookingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var confirmed = NewBooking(_now.AddDays(-2));
        confirmed.SubmitProof(Guid.NewGuid(), "p", _now.AddDays(-2));
        confirmed.Confirm("s", _now.AddHours(-1));
        db.Bookings.AddRange(confirmed, NewBooking(_now.AddHours(-3)), NewBooking(_now.AddDays(-20)));
        await db.SaveChangesAsync();

        var part = await new BookingMetricsProvider(db).GetAsync(new MetricsPeriod(_now.AddDays(-7), _now.AddDays(1), _cairo));

        part.Figures["bookings.created"].Should().Be(2);
        part.Figures["bookings.confirmed"].Should().Be(1);
        part.Figures["bookings.confirmed_value"].Should().Be(400);
        part.Series["bookings.created"].Should().HaveCount(9, "8 days starting mid-day touch 9 Cairo calendar days").And.Contain(p => p.Value == 1);
        part.Distributions["bookings.by_status"].Should().ContainKey(nameof(BookingStatus.PendingDeposit));
    }

    [Fact]
    public async Task Dashboard_MergesProviders_AndHidesMoneyFromNonFinanceStaffAsync()
    {
        IAdminMetricsProvider[] providers =
        [
            new Fixed(new Dictionary<string, decimal> { ["users.total"] = 10, ["mrr"] = 900, ["bookings.confirmed_value"] = 5000 }),
            new Fixed(new Dictionary<string, decimal> { ["venues.active"] = 3, ["revenue.collected"] = 500 })
        ];
        var period = new MetricsPeriod(_now.AddDays(-1), _now, _cairo);

        var support = await AdminDashboardEndpoints.BuildAsync(providers, period, new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 6), showFinance: false, default);
        var finance = await AdminDashboardEndpoints.BuildAsync(providers, period, new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 6), showFinance: true, default);

        support.Figures.Keys.Should().BeEquivalentTo("users.total", "venues.active");
        finance.Figures.Should().HaveCount(5).And.ContainKey("mrr");
    }

    private static BookingEntity NewBooking(DateTime createdAt) =>
        BookingEntity.Create(Guid.NewGuid(), "MAL-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            _now.AddDays(5), _now.AddDays(5).AddHours(1), Money.From(400), Money.From(100), createdAt.AddMinutes(30), null, null, createdAt, "p").Value;

    private sealed class Fixed(Dictionary<string, decimal> figures) : IAdminMetricsProvider
    {
        public Task<AdminMetricsPart> GetAsync(MetricsPeriod period, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminMetricsPart(figures, new Dictionary<string, IReadOnlyList<SeriesPoint>>(), new Dictionary<string, IReadOnlyDictionary<string, int>>()));
    }

    private sealed class NoUsage : IOwnerUsageReader
    {
        public Task<OwnerUsage> GetAsync(Guid ownerId, CancellationToken cancellationToken = default) => Task.FromResult(new OwnerUsage(0, 0, 0));
    }

    private sealed class Recorder : IAuditLog, INotificationPublisher
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Clock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
