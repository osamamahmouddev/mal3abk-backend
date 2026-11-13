using System.Text;
using FluentAssertions;
using Malabak.Modules.Subscriptions.Domain;
using Malabak.Modules.Subscriptions.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class SubscriptionAndInvoiceEdgeCaseTests
{
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc));

    private static SubscriptionsDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<SubscriptionsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SubscriptionsDbContext(options);
    }

    [Fact]
    public void SubscriptionPlan_Create_WithValidTerms_ShouldSucceed()
    {
        // Arrange
        var terms = new PlanTerms(
            Code: "PRO_MONTHLY",
            Name: "Professional Plan",
            Description: "For mid-size clubs",
            MonthlyPrice: 1500m,
            AnnualPrice: 15000m,
            MaxVenues: 3,
            MaxPitches: 10,
            MaxStaffPerVenue: 5,
            Features: ["analytics", "priority_support"],
            TrialDays: 14,
            GraceDays: 7,
            IsPublic: true,
            SortOrder: 1);

        // Act
        var result = SubscriptionPlan.Create(terms, _clock.UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var plan = result.Value;
        plan.Code.Should().Be("pro_monthly");
        plan.MonthlyPrice.Should().Be(1500m);
        plan.AnnualPrice.Should().Be(15000m);
        plan.MaxVenues.Should().Be(3);
        plan.MaxPitches.Should().Be(10);
        plan.MaxStaffPerVenue.Should().Be(5);
        plan.Features.Should().Contain("analytics");
        plan.Status.Should().Be(PlanStatus.Draft);
    }

    [Fact]
    public void SubscriptionPlan_LimitFor_ShouldEnforceConfiguredQuotas()
    {
        // Arrange
        var terms = new PlanTerms("STARTER", "Starter", null, 500m, 5000m, 1, 2, 1, [], 14, 7, true, 1);
        var plan = SubscriptionPlan.Create(terms, _clock.UtcNow).Value;

        // Act & Assert
        plan.LimitFor(EntitlementResource.Venues).Should().Be(1);
        plan.LimitFor(EntitlementResource.Pitches).Should().Be(2);
        plan.LimitFor(EntitlementResource.StaffPerVenue).Should().Be(1);
    }

    [Fact]
    public async Task EntitlementService_WhenSubscriptionSuspended_AllowsNoNewResourcesAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var ownerId = Guid.NewGuid();
        var plan = SubscriptionPlan.Create(new PlanTerms("PRO", "Pro", null, 1000m, 10000m, 5, 20, 10, ["api"], 14, 7, true, 1), _clock.UtcNow).Value;
        plan.SetStatus(PlanStatus.Active, _clock.UtcNow);
        db.Plans.Add(plan);

        var subscription = OwnerSubscription.Start(ownerId, plan, BillingCycle.Monthly, withTrial: true, Guid.NewGuid(), _clock.UtcNow);
        subscription.Suspend("Overdue payment investigation", Guid.NewGuid(), _clock.UtcNow);
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var service = new EntitlementService(db);

        // Act
        var venueCheck = await service.EnsureWithinLimitAsync(ownerId, EntitlementResource.Venues, currentCount: 0);
        var pitchCheck = await service.EnsureWithinLimitAsync(ownerId, EntitlementResource.Pitches, currentCount: 0);
        var hasFeature = await service.HasFeatureAsync(ownerId, "api");

        // Assert
        venueCheck.IsFailure.Should().BeTrue();
        venueCheck.Error.Should().Be(EntitlementErrors.SubscriptionInactive);

        pitchCheck.IsFailure.Should().BeTrue();
        pitchCheck.Error.Should().Be(EntitlementErrors.SubscriptionInactive);

        hasFeature.Should().BeFalse();
    }

    [Fact]
    public void Invoice_Void_TransitionsToVoidAndRecordsAuditMetadata()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        var payment = SubscriptionPayment.Create(
            Guid.NewGuid(), Guid.NewGuid(), 1500m, "EGP", PaymentMethod.BankTransfer,
            "TX-998811", _clock.UtcNow, _clock.UtcNow, _clock.UtcNow.AddMonths(1), adminId, _clock.UtcNow, "Initial payment");

        var plan = SubscriptionPlan.Create(new PlanTerms("PRO", "Pro", null, 1500m, 15000m, 3, 10, 5, [], 14, 7, true, 1), _clock.UtcNow).Value;
        var invoice = Invoice.Issue("INV-2026-000001", payment, plan, BillingCycle.Monthly, _clock.UtcNow);

        invoice.Status.Should().Be(InvoiceStatus.Paid);

        // Act
        var result = invoice.Void("Duplicate bank transfer entry recorded by mistake", adminId, _clock.UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        invoice.Status.Should().Be(InvoiceStatus.Void);
        invoice.VoidReason.Should().Be("Duplicate bank transfer entry recorded by mistake");
        invoice.VoidedBy.Should().Be(adminId);
        invoice.VoidedAtUtc.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public void Invoice_Void_WhenAlreadyVoided_ShouldFailInvalidTransition()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        var payment = SubscriptionPayment.Create(
            Guid.NewGuid(), Guid.NewGuid(), 1500m, "EGP", PaymentMethod.BankTransfer,
            "TX-998812", _clock.UtcNow, _clock.UtcNow, _clock.UtcNow.AddMonths(1), adminId, _clock.UtcNow, null);

        var plan = SubscriptionPlan.Create(new PlanTerms("PRO", "Pro", null, 1500m, 15000m, 3, 10, 5, [], 14, 7, true, 1), _clock.UtcNow).Value;
        var invoice = Invoice.Issue("INV-2026-000002", payment, plan, BillingCycle.Monthly, _clock.UtcNow);
        invoice.Void("First void", adminId, _clock.UtcNow);

        // Act: Voiding a second time
        var secondVoid = invoice.Void("Second void attempt", adminId, _clock.UtcNow);

        // Assert
        secondVoid.IsFailure.Should().BeTrue();
        secondVoid.Error.Should().Be(SubscriptionErrors.InvalidTransition);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Invoice_Void_WithEmptyReason_ShouldFailReasonRequired(string? emptyReason)
    {
        // Arrange
        var adminId = Guid.NewGuid();
        var payment = SubscriptionPayment.Create(
            Guid.NewGuid(), Guid.NewGuid(), 1500m, "EGP", PaymentMethod.BankTransfer,
            "TX-998813", _clock.UtcNow, _clock.UtcNow, _clock.UtcNow.AddMonths(1), adminId, _clock.UtcNow, null);

        var plan = SubscriptionPlan.Create(new PlanTerms("PRO", "Pro", null, 1500m, 15000m, 3, 10, 5, [], 14, 7, true, 1), _clock.UtcNow).Value;
        var invoice = Invoice.Issue("INV-2026-000003", payment, plan, BillingCycle.Monthly, _clock.UtcNow);

        // Act
        var result = invoice.Void(emptyReason!, adminId, _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SubscriptionErrors.ReasonRequired);
    }

    [Fact]
    public void InvoicePdf_Render_ShouldGenerateValidPdfDocumentBytes()
    {
        // Arrange
        var invoiceDto = new InvoiceDto(
            Id: Guid.NewGuid(),
            Number: "INV-2026-000042",
            OwnerId: Guid.NewGuid(),
            PlanCode: "PRO_ANNUAL",
            PlanName: "Pro Annual",
            Cycle: "Annual",
            Amount: 15000.00m,
            Currency: "EGP",
            IssuedAtUtc: _clock.UtcNow,
            PeriodStartUtc: _clock.UtcNow,
            PeriodEndUtc: _clock.UtcNow.AddYears(1),
            Status: "Paid",
            VoidReason: null);

        // Act
        var pdfBytes = InvoicePdf.Render(invoiceDto);

        // Assert: Valid PDF header and trailer
        pdfBytes.Should().NotBeNullOrEmpty();
        var pdfText = Encoding.ASCII.GetString(pdfBytes);
        pdfText.Should().StartWith("%PDF-1.4");
        pdfText.Should().Contain("%%EOF");
        pdfText.Should().Contain("INV-2026-000042");
        pdfText.Should().Contain("15000.00 EGP");
    }

    [Fact]
    public async Task InvoiceService_GetAsync_OtherOwnerCannotAccessInvoiceAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var legitimateOwnerId = Guid.NewGuid();
        var strangerOwnerId = Guid.NewGuid();

        var payment = SubscriptionPayment.Create(
            Guid.NewGuid(), legitimateOwnerId, 2000m, "EGP", PaymentMethod.InstaPay,
            "IP-1122", _clock.UtcNow, _clock.UtcNow, _clock.UtcNow.AddMonths(1), Guid.NewGuid(), _clock.UtcNow, null);

        var plan = SubscriptionPlan.Create(new PlanTerms("PLUS", "Plus", null, 2000m, 20000m, 2, 6, 3, [], 14, 7, true, 1), _clock.UtcNow).Value;
        var invoice = Invoice.Issue("INV-2026-000099", payment, plan, BillingCycle.Monthly, _clock.UtcNow);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var service = new InvoiceService(db, new FakeAuditLog(), _clock);

        // Act 1: Legitimate owner retrieves invoice
        var legitimateResult = await service.GetAsync(invoice.Id, legitimateOwnerId);
        // Act 2: Stranger owner attempts to retrieve invoice
        var strangerResult = await service.GetAsync(invoice.Id, strangerOwnerId);

        // Assert
        legitimateResult.IsSuccess.Should().BeTrue();
        legitimateResult.Value.Number.Should().Be("INV-2026-000099");

        strangerResult.IsFailure.Should().BeTrue();
        strangerResult.Error.Should().Be(SubscriptionErrors.InvoiceNotFound);
    }

    [Fact]
    public async Task InvoiceService_ReconcileAsync_CalculatesAccurateTotalsByMethodAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var ownerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var plan = SubscriptionPlan.Create(new PlanTerms("BASIC", "Basic", null, 500m, 5000m, 1, 2, 1, [], 14, 7, true, 1), _clock.UtcNow).Value;

        // Payment 1: BankTransfer 1000 EGP (Paid)
        var p1 = SubscriptionPayment.Create(Guid.NewGuid(), ownerId, 1000m, "EGP", PaymentMethod.BankTransfer, "BT01", _clock.UtcNow, _clock.UtcNow, _clock.UtcNow.AddMonths(1), adminId, _clock.UtcNow, null);
        var inv1 = Invoice.Issue("INV-2026-000101", p1, plan, BillingCycle.Monthly, _clock.UtcNow);

        // Payment 2: InstaPay 2000 EGP (Paid)
        var p2 = SubscriptionPayment.Create(Guid.NewGuid(), ownerId, 2000m, "EGP", PaymentMethod.InstaPay, "IP02", _clock.UtcNow, _clock.UtcNow, _clock.UtcNow.AddMonths(1), adminId, _clock.UtcNow, null);
        var inv2 = Invoice.Issue("INV-2026-000102", p2, plan, BillingCycle.Monthly, _clock.UtcNow);

        // Payment 3: MobileWallet 500 EGP (Voided)
        var p3 = SubscriptionPayment.Create(Guid.NewGuid(), ownerId, 500m, "EGP", PaymentMethod.MobileWallet, "VC03", _clock.UtcNow, _clock.UtcNow, _clock.UtcNow.AddMonths(1), adminId, _clock.UtcNow, null);
        var inv3 = Invoice.Issue("INV-2026-000103", p3, plan, BillingCycle.Monthly, _clock.UtcNow);
        inv3.Void("Mistake", adminId, _clock.UtcNow);

        db.Payments.AddRange(p1, p2, p3);
        db.Invoices.AddRange(inv1, inv2, inv3);
        await db.SaveChangesAsync();

        var service = new InvoiceService(db, new FakeAuditLog(), _clock);

        // Act
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var reconciliation = await service.ReconcileAsync(today.AddDays(-1), today.AddDays(1));

        // Assert
        reconciliation.Invoices.Should().Be(3);
        reconciliation.Collected.Should().Be(3000m); // 1000 + 2000
        reconciliation.Voided.Should().Be(500m);
        reconciliation.ByMethod.Should().Contain(m => m.Method == "InstaPay" && m.Amount == 2000m && m.Count == 1);
        reconciliation.ByMethod.Should().Contain(m => m.Method == "BankTransfer" && m.Amount == 1000m && m.Count == 1);
    }

    private sealed class FakeAuditLog : IAuditLog
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
