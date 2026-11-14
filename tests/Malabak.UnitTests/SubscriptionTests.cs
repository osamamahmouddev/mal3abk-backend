using System.Text;
using FluentAssertions;
using Malabak.Modules.Subscriptions.Domain;
using Malabak.Modules.Subscriptions.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class SubscriptionTests
{
    private static readonly DateTime _now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly MutableClock _clock = new();
    private readonly Recorder _recorder = new();
    private readonly FakeUsage _usage = new();

    [Fact]
    public async Task OwnersWithoutPlans_AreNotLimited_UntilAnAdminSetsADefaultPlanAsync()
    {
        await using var db = NewDb();
        var entitlements = new EntitlementService(db);

        (await entitlements.EnsureWithinLimitAsync(_owner, EntitlementResource.Venues, 50)).IsSuccess.Should().BeTrue("no plans configured yet");

        var starter = await ActivePlanAsync(db, "starter", maxVenues: 1, maxPitches: 3);
        await Plans(db).MakeDefaultAsync(starter.Id, _admin);

        (await entitlements.EnsureWithinLimitAsync(_owner, EntitlementResource.Venues, 0)).IsSuccess.Should().BeTrue();
        (await entitlements.EnsureWithinLimitAsync(_owner, EntitlementResource.Venues, 1)).Error.Should().Be(EntitlementErrors.QuotaExceeded);
        (await entitlements.EnsureWithinLimitAsync(_owner, EntitlementResource.Pitches, 3)).Error.Should().Be(EntitlementErrors.QuotaExceeded);
        (await entitlements.EnsureWithinLimitAsync(_owner, EntitlementResource.StaffPerVenue, 100)).IsSuccess.Should().BeTrue("no staff limit on this plan");
    }

    [Fact]
    public async Task ExpiredOrSuspendedSubscriptions_BlockNewResources_ButNotTheGracePeriodAsync()
    {
        await using var db = NewDb();
        var pro = await ActivePlanAsync(db, "pro", maxVenues: 5, graceDays: 3);
        var service = Subscriptions(db);
        await service.AssignAsync(_owner, pro.Id, BillingCycle.Monthly, withTrial: false, _admin);
        var entitlements = new EntitlementService(db);
        var job = Job(db);

        _clock.UtcNow = _now.AddMonths(1).AddHours(1);
        await job.RunAsync();
        (await db.Subscriptions.SingleAsync()).Status.Should().Be(SubscriptionStatus.Grace);
        (await entitlements.EnsureWithinLimitAsync(_owner, EntitlementResource.Venues, 0)).IsSuccess.Should().BeTrue("grace keeps the account working");

        _clock.UtcNow = _now.AddMonths(1).AddDays(4);
        await job.RunAsync();
        (await db.Subscriptions.SingleAsync()).Status.Should().Be(SubscriptionStatus.Expired);
        (await entitlements.EnsureWithinLimitAsync(_owner, EntitlementResource.Venues, 0)).Error.Should().Be(EntitlementErrors.SubscriptionInactive);
        _recorder.Notifications.Select(n => n.Type).Should().Contain([NotificationTypes.SubscriptionGraceStarted, NotificationTypes.SubscriptionExpired]);
    }

    [Fact]
    public async Task ExpiryWarnings_AreSentOncePerPeriod_SevenDaysAndOneDayBeforeAsync()
    {
        await using var db = NewDb();
        var plan = await ActivePlanAsync(db, "pro");
        await Subscriptions(db).AssignAsync(_owner, plan.Id, BillingCycle.Monthly, false, _admin);
        var job = Job(db);
        var end = _now.AddMonths(1);

        _clock.UtcNow = end.AddDays(-6);
        await job.RunAsync();
        await job.RunAsync();
        _clock.UtcNow = end.AddHours(-12);
        await job.RunAsync();
        await job.RunAsync();

        _recorder.Notifications.Where(n => n.Type == NotificationTypes.SubscriptionExpiring).Should().HaveCount(2, "one 7-day and one 1-day warning, no repeats");
    }

    [Fact]
    public async Task RecordingAPayment_ExtendsThePeriod_IssuesANumberedInvoice_NotifiesAndAuditsAsync()
    {
        await using var db = NewDb();
        var plan = await ActivePlanAsync(db, "pro", trialDays: 14);
        var service = Subscriptions(db);
        await service.AssignAsync(_owner, plan.Id, BillingCycle.Monthly, withTrial: true, _admin);
        (await db.Subscriptions.SingleAsync()).Status.Should().Be(SubscriptionStatus.Trial);

        var invoice = (await service.RecordPaymentAsync(_owner, new RecordPaymentInput(500, PaymentMethod.InstaPay, "IP-123", null, null), _admin)).Value;

        invoice.Number.Should().Be("INV-2026-000001");
        invoice.Amount.Should().Be(500);
        var subscription = await db.Subscriptions.SingleAsync();
        subscription.Status.Should().Be(SubscriptionStatus.Active);
        subscription.CurrentPeriodEndUtc.Should().Be(_now.AddMonths(1), "the trial ended early by payment; the paid month starts now");
        _recorder.Notifications.Should().Contain(n => n.Type == NotificationTypes.SubscriptionPaymentRecorded && n.UserId == _owner);
        _recorder.Audits.Should().Contain(a => a.Action == "subscription.payment_recorded");

        var second = (await service.RecordPaymentAsync(_owner, new RecordPaymentInput(500, PaymentMethod.Cash, null, null, null), _admin)).Value;
        second.Number.Should().Be("INV-2026-000002");
        (await db.Subscriptions.SingleAsync()).CurrentPeriodEndUtc.Should().Be(_now.AddMonths(2), "paying early adds to the current period");

        (await service.RecordPaymentAsync(_owner, new RecordPaymentInput(0, PaymentMethod.Cash, null, null, null), _admin)).Error.Should().Be(SubscriptionErrors.InvalidPayment);
    }

    [Fact]
    public async Task Downgrades_MustFitCurrentUsage_AndArchivedPlansCannotBeAssignedAsync()
    {
        await using var db = NewDb();
        var pro = await ActivePlanAsync(db, "pro", maxVenues: 5);
        var basic = await ActivePlanAsync(db, "basic", maxVenues: 1);
        var service = Subscriptions(db);
        await service.AssignAsync(_owner, pro.Id, BillingCycle.Monthly, false, _admin);
        _usage.Current = new OwnerUsage(Venues: 2, Pitches: 4, MaxStaffInOneVenue: 1);

        (await service.ChangePlanAsync(_owner, basic.Id, BillingCycle.Monthly, null, _admin)).Error.Should().Be(SubscriptionErrors.DowngradeNotPossible);

        _usage.Current = new OwnerUsage(1, 2, 1);
        (await service.ChangePlanAsync(_owner, basic.Id, BillingCycle.Annual, "customer asked", _admin)).Value.Plan.Code.Should().Be("basic");

        await Plans(db).SetStatusAsync(pro.Id, PlanStatus.Archived, _admin);
        (await service.ChangePlanAsync(_owner, pro.Id, BillingCycle.Monthly, null, _admin)).Error.Should().Be(PlanErrors.NotAssignable);
        (await service.AssignAsync(_owner, basic.Id, BillingCycle.Monthly, false, _admin)).Error.Should().Be(SubscriptionErrors.AlreadySubscribed);
    }

    [Fact]
    public async Task SuspendAndReactivate_RequireAReason_AndAreRecordedInHistoryAsync()
    {
        await using var db = NewDb();
        var plan = await ActivePlanAsync(db, "pro");
        var service = Subscriptions(db);
        await service.AssignAsync(_owner, plan.Id, BillingCycle.Monthly, false, _admin);

        (await service.SuspendAsync(_owner, "  ", _admin)).Error.Should().Be(SubscriptionErrors.ReasonRequired);
        (await service.SuspendAsync(_owner, "Chargeback", _admin)).Value.AllowsNewResources.Should().BeFalse();
        (await service.ReactivateAsync(_owner, "Resolved", _admin)).Value.Status.Should().Be(nameof(SubscriptionStatus.Active));

        var detail = (await service.GetByOwnerAsync(_owner)).Value;
        detail.History.Select(h => h.Type).Should().ContainInOrder(nameof(SubscriptionEventType.Assigned), nameof(SubscriptionEventType.Suspended), nameof(SubscriptionEventType.Reactivated));
    }

    [Fact]
    public async Task Plans_HaveUniqueCodes_AndTheDefaultMustStayActiveAsync()
    {
        await using var db = NewDb();
        var plans = Plans(db);
        var starter = await ActivePlanAsync(db, "starter");

        (await plans.CreateAsync(Terms("starter"), _admin)).Error.Should().Be(PlanErrors.DuplicateCode);
        (await plans.CreateAsync(Terms("x", monthly: -1), _admin)).Error.Should().Be(PlanErrors.Invalid);

        await plans.MakeDefaultAsync(starter.Id, _admin);
        (await plans.SetStatusAsync(starter.Id, PlanStatus.Archived, _admin)).Error.Should().Be(PlanErrors.DefaultMustBeActive);
        (await plans.ListPublicAsync()).Should().ContainSingle().Which.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Invoices_AreOnlyVisibleToTheirOwner_RenderAsAValidPdf_AndVoidingKeepsThemAsync()
    {
        await using var db = NewDb();
        var plan = await ActivePlanAsync(db, "pro");
        var service = Subscriptions(db);
        await service.AssignAsync(_owner, plan.Id, BillingCycle.Monthly, false, _admin);
        var invoice = (await service.RecordPaymentAsync(_owner, new RecordPaymentInput(500, PaymentMethod.BankTransfer, "TRX(1)", null, null), _admin)).Value;
        var invoices = new InvoiceService(db, _recorder, _clock);

        (await invoices.GetAsync(invoice.Id, Guid.NewGuid())).Error.Should().Be(SubscriptionErrors.InvoiceNotFound, "another owner cannot read it");
        var pdf = (await invoices.PdfAsync(invoice.Id, _owner)).Value;
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().StartWith("%PDF-1.4").And.EndWith("%%EOF\n").And.Contain(invoice.Number).And.Contain("500.00 EGP");
        var xref = int.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0], System.Globalization.CultureInfo.InvariantCulture);
        text[xref..].Should().StartWith("xref", "the cross-reference offset must point at the table");

        (await invoices.VoidAsync(invoice.Id, "Recorded twice", _admin)).Value.Status.Should().Be(nameof(InvoiceStatus.Void));
        var reconciliation = await invoices.ReconcileAsync(DateOnly.FromDateTime(_now), DateOnly.FromDateTime(_now));
        reconciliation.Should().Match<ReconciliationDto>(r => r.Invoices == 1 && r.Collected == 0 && r.Voided == 500);
    }

    [Theory]
    [InlineData("Plan (pro)", "Plan \\(pro\\)")]
    [InlineData("باقة", "????")]
    [InlineData("a\\b", "a\\\\b")]
    public void PdfText_IsEscaped_AndNonLatinBecomesPlaceholders(string input, string expected)
    {
        InvoicePdf.Escape(input).Should().Be(expected);
    }

    [Fact]
    public async Task VenueOwner_CanRequestPlanUpgrade_AndAdminCanReplyAndApproveAsync()
    {
        await using var db = NewDb();
        var growth = await ActivePlanAsync(db, "growth", maxVenues: 3);
        var service = Subscriptions(db);

        // Owner requests upgrade to growth plan
        var reqResult = await service.CreateOrUpdateChangeRequestAsync(_owner, growth.Id, BillingCycle.Monthly, "عاوز ادفع فودافون كاش");
        reqResult.IsSuccess.Should().BeTrue();
        reqResult.Value.Status.Should().Be("Pending");
        reqResult.Value.PlanName.Should().Be(growth.Name);
        reqResult.Value.Amount.Should().Be(growth.MonthlyPrice);

        // Check latest request
        var latest = await service.GetLatestRequestForOwnerAsync(_owner);
        latest.Should().NotBeNull();
        latest!.Id.Should().Be(reqResult.Value.Id);
        latest.Status.Should().Be("Pending");

        // Admin replies with payment instructions
        var replyResult = await service.ReplyToRequestAsync(reqResult.Value.Id, "حوّل 500 ج.م على فودافون كاش 01012345678", _admin);
        replyResult.IsSuccess.Should().BeTrue();
        _recorder.Notifications.Should().Contain(n => n.Type == NotificationTypes.SubscriptionRequestInstructions);

        // Admin approves request
        var approveResult = await service.ApproveRequestAsync(reqResult.Value.Id, _admin);
        approveResult.IsSuccess.Should().BeTrue();
        _recorder.Notifications.Should().Contain(n => n.Type == NotificationTypes.SubscriptionRequestApproved);

        // Verify owner subscription is now active on growth plan
        var billing = await service.GetOwnerBillingAsync(_owner);
        billing.Subscription.Should().NotBeNull();
        billing.Subscription!.Plan.Code.Should().Be(growth.Code);
    }

    [Fact]
    public async Task AssignOrChangeAsync_AssignsNew_OrChangesExistingSubscriptionAsync()
    {
        await using var db = NewDb();
        var starter = await ActivePlanAsync(db, "starter", maxVenues: 1);
        var growth = await ActivePlanAsync(db, "growth", maxVenues: 5);
        var service = Subscriptions(db);

        // 1. Assign for first time
        var assignResult = await service.AssignOrChangeAsync(_owner, starter.Id, BillingCycle.Monthly, withTrial: true, _admin);
        assignResult.IsSuccess.Should().BeTrue();
        assignResult.Value.Plan.Code.Should().Be(starter.Code);

        // 2. Assign/Change again without AlreadySubscribed error
        var changeResult = await service.AssignOrChangeAsync(_owner, growth.Id, BillingCycle.Annual, withTrial: false, _admin);
        changeResult.IsSuccess.Should().BeTrue();
        changeResult.Value.Plan.Code.Should().Be(growth.Code);
        changeResult.Value.Cycle.Should().Be("Annual");
    }

    private PlanService Plans(SubscriptionsDbContext db) => new(db, _recorder, _clock);

    private SubscriptionService Subscriptions(SubscriptionsDbContext db) => new(db, _usage, new InvoiceNumberGenerator(db), _recorder, _recorder, _clock);

    private SubscriptionLifecycleJob Job(SubscriptionsDbContext db) => new(db, _recorder, _recorder, _clock, NullLogger<SubscriptionLifecycleJob>.Instance);

    private async Task<PlanDto> ActivePlanAsync(SubscriptionsDbContext db, string code, int? maxVenues = null, int? maxPitches = null, int trialDays = 0, int graceDays = 0)
    {
        var plans = Plans(db);
        var plan = (await plans.CreateAsync(Terms(code, maxVenues: maxVenues, maxPitches: maxPitches, trialDays: trialDays, graceDays: graceDays), _admin)).Value;
        return (await plans.SetStatusAsync(plan.Id, PlanStatus.Active, _admin)).Value;
    }

    private static PlanTerms Terms(string code, decimal monthly = 500, int? maxVenues = null, int? maxPitches = null, int trialDays = 0, int graceDays = 0) =>
        new(code, code.ToUpperInvariant(), null, monthly, monthly * 10, maxVenues, maxPitches, null, [PlanFeatures.CsvExport], trialDays, graceDays, true, 0);

    private static SubscriptionsDbContext NewDb() => new(new DbContextOptionsBuilder<SubscriptionsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeUsage : IOwnerUsageReader
    {
        public OwnerUsage Current { get; set; } = new(0, 0, 0);

        public Task<OwnerUsage> GetAsync(Guid ownerId, CancellationToken cancellationToken = default) => Task.FromResult(Current);
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

    private sealed class MutableClock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        public DateTime UtcNow { get; set; } = _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
