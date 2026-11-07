using System.Text.Json.Nodes;
using FluentAssertions;
using Malabak.Modules.Audit.Application;
using Malabak.Modules.Audit.Infrastructure;
using Malabak.Modules.Subscriptions.Domain;
using Malabak.Modules.Subscriptions.Infrastructure;
using Malabak.Modules.Venues.Application.Subscriptions;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>Deeper edge cases for TASK-062 (audit) and TASK-060/707/708/709 (plans, entitlements, lifecycle, invoices).</summary>
public sealed class Phase6AuditSubscriptionEdgeTests
{
    private static readonly DateTime _now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _admin = Guid.NewGuid();
    private readonly Clock _clock = new();
    private readonly Recorder _recorder = new();

    // ---------- TASK-062: audit ----------

    [Theory]
    [InlineData("password", "hunter2")]
    [InlineData("Password", "x")]
    [InlineData("refresh_token", "abc")]
    [InlineData("otpCode", "1234")]
    [InlineData("passwordHash", "$2a$")]
    [InlineData("access-token", "eyJ")]
    public void Secrets_AreFullyMasked_WhateverTheirSpelling(string key, string value)
    {
        var json = AuditMasking.Serialize(new JsonObject { [key] = value })!;

        json.Should().NotContain(value).And.Contain(AuditMasking.Masked);
    }

    [Theory]
    [InlineData("phoneNumber", "+201012345678", "***5678")]
    [InlineData("email", "owner@example.com", "***.com")]
    [InlineData("iban", "EG380019000500000000263180002", "***0002")]
    [InlineData("walletNumber", "01012345", "***45")]
    [InlineData("phone", "123", "***")]
    public void ContactAndBankDetails_KeepOnlyTheirLastCharacters(string key, string value, string expected)
    {
        var json = JsonNode.Parse(AuditMasking.Serialize(new JsonObject { [key] = value })!)!;

        json[key]!.GetValue<string>().Should().Be(expected);
    }

    [Fact]
    public void Masking_ReachesNestedObjectsAndArrays_AndLeavesOrdinaryFieldsAlone()
    {
        var snapshot = new
        {
            Status = "Suspended",
            Owner = new { Name = "Karim", PhoneNumber = "+201012345678" },
            Methods = new[] { new { Type = "InstaPay", AccountDetails = "karim@instapay" } },
            Credentials = new { Password = "secret" }
        };

        var json = AuditMasking.Serialize(snapshot)!;

        json.Should().Contain("Suspended").And.Contain("Karim").And.Contain("***5678").And.Contain("***apay");
        json.Should().NotContain("secret").And.NotContain("+201012345678").And.NotContain("karim@instapay");
    }

    [Fact]
    public void NullSnapshots_StayNull()
    {
        AuditMasking.Serialize(null).Should().BeNull();
    }

    [Fact]
    public async Task RecordingNeverThrows_EvenWhenTheStoreIsBrokenAsync()
    {
        var db = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.DisposeAsync();
        var service = new AuditLogService(db, new NoRequest(), _clock, NullLogger<AuditLogService>.Instance);

        var act = () => service.RecordAsync(new AuditEntry("admin.test", "X", "1", _admin, AuditActorType.Admin));

        await act.Should().NotThrowAsync("an audit failure must not break the action being audited");
    }

    [Fact]
    public async Task HugeSnapshots_AreTruncated_AndPagingIsBoundedAsync()
    {
        await using var db = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new AuditLogService(db, new NoRequest(), _clock, NullLogger<AuditLogService>.Instance);
        await service.RecordAsync(new AuditEntry("admin.big", "X", "1", _admin, AuditActorType.Admin, After: new { Blob = new string('x', 50_000) }));
        var query = new AuditLogQuery(db);

        var row = (await query.SearchAsync(new AuditLogFilter())).Value.Items.Single();

        row.AfterJson!.Length.Should().BeLessThanOrEqualTo(16_000);
        (await query.SearchAsync(new AuditLogFilter(Page: 0))).IsFailure.Should().BeTrue();
        (await query.SearchAsync(new AuditLogFilter(PageSize: 0))).IsFailure.Should().BeTrue();
    }

    // ---------- TASK-707/708: plans and lifecycle ----------

    [Fact]
    public async Task AnnualPayments_AddAYear_AndListingsFilterByStatusAndExpiryAsync()
    {
        await using var db = SubsDb();
        var plan = await ActivePlanAsync(db, "pro");
        var service = Subs(db);
        var annual = Guid.NewGuid();
        var monthly = Guid.NewGuid();
        await service.AssignAsync(annual, plan.Id, BillingCycle.Annual, false, _admin);
        await service.AssignAsync(monthly, plan.Id, BillingCycle.Monthly, false, _admin);

        await service.RecordPaymentAsync(annual, new RecordPaymentInput(4800, PaymentMethod.BankTransfer, "T-1", null, null), _admin);

        var paid = await db.Subscriptions.SingleAsync(s => s.OwnerId == annual);
        paid.CurrentPeriodEndUtc.Should().Be(_now.AddYears(1), "the first payment pays the running year; it does not add a second one");
        paid.CurrentPeriodPaid.Should().BeTrue();
        (await service.ListAsync(SubscriptionStatus.Active, null, null)).Should().HaveCount(2);
        (await service.ListAsync(null, null, 40)).Select(s => s.OwnerId).Should().Equal(monthly);
        (await service.ListAsync(SubscriptionStatus.Trial, null, null)).Should().BeEmpty();
    }

    [Fact]
    public async Task APaymentBringsAnExpiredSubscriptionBack_AndCancelledOnesStayCancelledAsync()
    {
        await using var db = SubsDb();
        var plan = await ActivePlanAsync(db, "pro", graceDays: 1);
        var service = Subs(db);
        var owner = Guid.NewGuid();
        await service.AssignAsync(owner, plan.Id, BillingCycle.Monthly, false, _admin);
        var job = new SubscriptionLifecycleJob(db, _recorder, _recorder, _clock, NullLogger<SubscriptionLifecycleJob>.Instance);
        _clock.UtcNow = _now.AddMonths(1).AddHours(1);
        await job.RunAsync();
        _clock.UtcNow = _now.AddMonths(1).AddDays(3);
        await job.RunAsync();
        (await db.Subscriptions.SingleAsync()).Status.Should().Be(SubscriptionStatus.Expired);

        await service.RecordPaymentAsync(owner, new RecordPaymentInput(500, PaymentMethod.Cash, null, null, null), _admin);
        var renewed = await db.Subscriptions.SingleAsync();
        renewed.Status.Should().Be(SubscriptionStatus.Active);
        renewed.CurrentPeriodEndUtc.Should().Be(_clock.UtcNow.AddMonths(1), "an expired subscription restarts from the payment day");

        (await service.CancelAsync(owner, "Closed the business", _admin)).Value.Status.Should().Be(nameof(SubscriptionStatus.Cancelled));
        (await service.RecordPaymentAsync(owner, new RecordPaymentInput(500, PaymentMethod.Cash, null, null, null), _admin)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task TheLifecycleJob_IsIdempotent_WhenRunTwiceAsync()
    {
        await using var db = SubsDb();
        var plan = await ActivePlanAsync(db, "pro", graceDays: 2);
        await Subs(db).AssignAsync(Guid.NewGuid(), plan.Id, BillingCycle.Monthly, false, _admin);
        var job = new SubscriptionLifecycleJob(db, _recorder, _recorder, _clock, NullLogger<SubscriptionLifecycleJob>.Instance);
        _clock.UtcNow = _now.AddMonths(1).AddHours(1);

        var first = await job.RunAsync();
        var second = await job.RunAsync();

        first.Should().BeGreaterThan(0);
        second.Should().Be(0);
        _recorder.Notifications.Count(n => n.Type == NotificationTypes.SubscriptionGraceStarted).Should().Be(1);
    }

    [Fact]
    public async Task InvoiceNumbers_AreSequential_AndFormattedPerYearAsync()
    {
        await using var db = SubsDb();
        var generator = new InvoiceNumberGenerator(db);

        (await generator.NextAsync(2026)).Should().MatchRegex(@"^INV-2026-\d{6}$");
        (await generator.NextAsync(2027)).Should().StartWith("INV-2027-");
    }

    [Fact]
    public async Task OwnersOnlySeeTheirOwnBilling_AndUsageComesFromTheVenuesModuleAsync()
    {
        await using var db = SubsDb();
        var plan = await ActivePlanAsync(db, "pro");
        var usage = new FixedUsage(new OwnerUsage(2, 5, 3));
        var service = new SubscriptionService(db, usage, new InvoiceNumberGenerator(db), _recorder, _recorder, _clock);
        var owner = Guid.NewGuid();
        await service.AssignAsync(owner, plan.Id, BillingCycle.Monthly, false, _admin);
        await service.RecordPaymentAsync(owner, new RecordPaymentInput(500, PaymentMethod.Cash, null, null, null), _admin);

        var mine = await service.GetOwnerBillingAsync(owner);
        var stranger = await service.GetOwnerBillingAsync(Guid.NewGuid());

        mine.Invoices.Should().ContainSingle();
        mine.Usage.Should().Be(new OwnerUsage(2, 5, 3));
        stranger.Invoices.Should().BeEmpty();
        stranger.Subscription.Should().BeNull();
    }

    [Fact]
    public async Task Reconciliation_RejectsRangesLongerThanAYearAtTheEndpoint_AndGroupsByMethodAsync()
    {
        await using var db = SubsDb();
        var plan = await ActivePlanAsync(db, "pro");
        var service = Subs(db);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await service.AssignAsync(a, plan.Id, BillingCycle.Monthly, false, _admin);
        await service.AssignAsync(b, plan.Id, BillingCycle.Monthly, false, _admin);
        await service.RecordPaymentAsync(a, new RecordPaymentInput(500, PaymentMethod.Cash, null, null, null), _admin);
        await service.RecordPaymentAsync(b, new RecordPaymentInput(500, PaymentMethod.InstaPay, null, null, null), _admin);

        var r = await new InvoiceService(db, _recorder, _clock).ReconcileAsync(DateOnly.FromDateTime(_now), DateOnly.FromDateTime(_now));

        r.Collected.Should().Be(1000);
        r.ByMethod.Should().HaveCount(2).And.OnlyContain(m => m.Amount == 500 && m.Count == 1);
    }

    // ---------- TASK-060: entitlements through the real handlers ----------

    [Fact]
    public async Task StaffLimits_ComeFromThePlan_ButTheHardCapAlwaysApplysAsync()
    {
        await using var subs = SubsDb();
        await using var venues = VenuesDb();
        var owner = Guid.NewGuid();
        var venue = Venue.Create(Guid.NewGuid(), "V", "v", null, owner, "a", "Cairo", "Maadi", null, null, "+201000000000", null, null, _now);
        venues.Venues.Add(venue);
        await venues.SaveChangesAsync();
        var plans = new PlanService(subs, _recorder, _clock);
        var small = (await plans.CreateAsync(new PlanTerms("small", "Small", null, 100, 1000, null, null, 2, [], 0, 0, true, 0), _admin)).Value;
        await plans.SetStatusAsync(small.Id, PlanStatus.Active, _admin);
        await Subs(subs).AssignAsync(owner, small.Id, BillingCycle.Monthly, false, _admin);
        var policy = new EntitlementStaffQuotaPolicy(venues, new EntitlementService(subs));

        (await policy.CanAddAsync(venue.Id, 1)).Should().BeTrue();
        (await policy.CanAddAsync(venue.Id, 2)).Should().BeFalse("the plan allows 2 staff per venue");
        (await policy.CanAddAsync(Guid.NewGuid(), 0)).Should().BeFalse("unknown venues never get staff");

        var unlimited = (await plans.CreateAsync(new PlanTerms("big", "Big", null, 100, 1000, null, null, null, [], 0, 0, true, 0), _admin)).Value;
        await plans.SetStatusAsync(unlimited.Id, PlanStatus.Active, _admin);
        await Subs(subs).ChangePlanAsync(owner, unlimited.Id, BillingCycle.Monthly, null, _admin);
        (await policy.CanAddAsync(venue.Id, 24)).Should().BeTrue();
        (await policy.CanAddAsync(venue.Id, 25)).Should().BeFalse("the platform-wide cap of 25 applies to every plan");
    }

    [Fact]
    public async Task Features_FollowTheEffectivePlan_AndInactiveSubscriptionsLoseThemNotAsync()
    {
        await using var db = SubsDb();
        var plan = await ActivePlanAsync(db, "pro", features: [PlanFeatures.CsvExport]);
        var owner = Guid.NewGuid();
        var entitlements = new EntitlementService(db);

        (await entitlements.HasFeatureAsync(owner, PlanFeatures.CsvExport)).Should().BeTrue("with no default plan nothing is limited");

        await Subs(db).AssignAsync(owner, plan.Id, BillingCycle.Monthly, false, _admin);
        (await entitlements.HasFeatureAsync(owner, PlanFeatures.CsvExport)).Should().BeTrue();
        (await entitlements.HasFeatureAsync(owner, PlanFeatures.PrioritySupport)).Should().BeFalse();
    }

    private async Task<PlanDto> ActivePlanAsync(SubscriptionsDbContext db, string code, int graceDays = 0, IReadOnlyList<string>? features = null)
    {
        var plans = new PlanService(db, _recorder, _clock);
        var plan = (await plans.CreateAsync(new PlanTerms(code, code, null, 500, 4800, null, null, null, features ?? [], 0, graceDays, true, 0), _admin)).Value;
        return (await plans.SetStatusAsync(plan.Id, PlanStatus.Active, _admin)).Value;
    }

    private SubscriptionService Subs(SubscriptionsDbContext db) => new(db, new FixedUsage(new OwnerUsage(0, 0, 0)), new InvoiceNumberGenerator(db), _recorder, _recorder, _clock);

    private static SubscriptionsDbContext SubsDb() => new(new DbContextOptionsBuilder<SubscriptionsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static VenuesDbContext VenuesDb() => new(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedUsage(OwnerUsage usage) : IOwnerUsageReader
    {
        public Task<OwnerUsage> GetAsync(Guid ownerId, CancellationToken cancellationToken = default) => Task.FromResult(usage);
    }

    private sealed class NoRequest : IAuditRequestContext
    {
        public string? IpAddress => null;
        public Guid? ImpersonatorId => null;
        public string? CorrelationId => null;
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

        public DateTime UtcNow { get; set; } = _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
