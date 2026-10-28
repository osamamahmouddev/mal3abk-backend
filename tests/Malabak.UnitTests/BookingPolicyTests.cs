using FluentAssertions;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Policies;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.Modules.Venues.Infrastructure.Services;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Malabak.UnitTests;

public sealed class BookingPolicyTests
{
    private static readonly DateTime _now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly MutableClock _clock = new();

    private static BookingPolicyTerms Terms(int cancellationHours = 24, RefundEligibility refund = RefundEligibility.FullRefund, decimal refundPct = 0) => new(
        DepositRule.Create(DepositType.Percentage, 30), 60, 120, cancellationHours, refund, refundPct,
        NoShowRule.ForfeitDeposit, 10, AllowReschedule: true, RescheduleDeadlineHours: 12, PendingBookingsBlockSlots: true, "No metal studs");

    [Fact]
    public void FirstVersion_IsCurrent_Version1()
    {
        var policy = VenuePolicy.CreateVersion(Guid.NewGuid(), null, Terms(), _ownerId, _now);

        policy.Version.Should().Be(1);
        policy.IsCurrent.Should().BeTrue();
        policy.RefundPercentage.Should().Be(100);
    }

    [Fact]
    public void NewVersion_SupersedesPrevious_WithoutChangingItsValues()
    {
        var venueId = Guid.NewGuid();
        var v1 = VenuePolicy.CreateVersion(venueId, null, Terms(cancellationHours: 24), _ownerId, _now);
        var snapshotTakenByBooking = v1.ToSnapshot();

        var v2 = VenuePolicy.CreateVersion(venueId, v1, Terms(cancellationHours: 48), _ownerId, _now.AddDays(1));

        v1.IsCurrent.Should().BeFalse();
        v1.SupersededAtUtc.Should().Be(_now.AddDays(1));
        v1.CancellationDeadlineHours.Should().Be(24);
        v2.Version.Should().Be(2);
        v2.CancellationDeadlineHours.Should().Be(48);
        snapshotTakenByBooking.CancellationDeadlineHours.Should().Be(24, "an existing booking keeps the rules it was made under");
        snapshotTakenByBooking.Version.Should().Be(1);
    }

    [Fact]
    public void SupersedingNonCurrentOrOtherVenuePolicy_Throws()
    {
        var v1 = VenuePolicy.CreateVersion(Guid.NewGuid(), null, Terms(), _ownerId, _now);

        var otherVenue = () => VenuePolicy.CreateVersion(Guid.NewGuid(), v1, Terms(), _ownerId, _now);

        otherVenue.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(5, 120, 24, 10)]
    [InlineData(60, 5, 24, 10)]
    [InlineData(60, 120, 200, 10)]
    [InlineData(60, 120, 24, 90)]
    public void Validation_RejectsOutOfRangeWindows(int proof, int review, int cancel, int grace)
    {
        var terms = Terms() with
        {
            ProofSubmissionWindowMinutes = proof,
            ManualReviewWindowMinutes = review,
            CancellationDeadlineHours = cancel,
            LateArrivalGraceMinutes = grace
        };

        terms.Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void PartialRefund_RequiresPercentageBetween1And99()
    {
        Terms(refund: RefundEligibility.PartialRefund, refundPct: 0).Validate().Should().NotBeEmpty();
        Terms(refund: RefundEligibility.PartialRefund, refundPct: 50).Validate().Should().BeEmpty();
    }

    [Fact]
    public async Task SetHandler_CreatesVersions_AndProviderReturnsCurrentAsync()
    {
        await using var db = NewDb();
        var venue = await SeedVenueAsync(db, approvedAndPublished: false);
        var handler = new SetBookingPolicyHandler(new VenueAccessService(db), new NoopAudit(), db, _clock);

        var v1 = await handler.HandleAsync(new SetBookingPolicyCommand(venue.Id, _ownerId, Terms(cancellationHours: 24)));
        _clock.UtcNow = _now.AddDays(1);
        var v2 = await handler.HandleAsync(new SetBookingPolicyCommand(venue.Id, _ownerId, Terms(cancellationHours: 6)));
        var snapshot = await new BookingPolicyProvider(db).GetCurrentSnapshotAsync(venue.Id);
        var history = await new GetBookingPoliciesHandler(new VenueAccessService(db), db).HandleAsync(new GetBookingPoliciesQuery(venue.Id, _ownerId));

        v1.Value.Version.Should().Be(1);
        v2.Value.Version.Should().Be(2);
        snapshot!.Version.Should().Be(2);
        snapshot.CancellationDeadlineHours.Should().Be(6);
        history.Value.Select(h => (h.Version, h.IsCurrent)).Should().Equal((2, true), (1, false));
    }

    [Fact]
    public async Task SetHandler_NonOwner_Or_InvalidTerms_FailsAsync()
    {
        await using var db = NewDb();
        var venue = await SeedVenueAsync(db, approvedAndPublished: false);
        var handler = new SetBookingPolicyHandler(new VenueAccessService(db), new NoopAudit(), db, _clock);

        var stranger = await handler.HandleAsync(new SetBookingPolicyCommand(venue.Id, Guid.NewGuid(), Terms()));
        var invalid = await handler.HandleAsync(new SetBookingPolicyCommand(venue.Id, _ownerId, Terms(cancellationHours: 500)));

        stranger.Error.Code.Should().Be("Venue.NotFound");
        invalid.Error.Code.Should().Be("BookingPolicy.Invalid");
        (await db.Policies.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Preview_OnlyForApprovedAndPublishedVenuesAsync()
    {
        await using var db = NewDb();
        var hidden = await SeedVenueAsync(db, approvedAndPublished: false);
        var visible = await SeedVenueAsync(db, approvedAndPublished: true);
        var setter = new SetBookingPolicyHandler(new VenueAccessService(db), new NoopAudit(), db, _clock);
        await setter.HandleAsync(new SetBookingPolicyCommand(hidden.Id, _ownerId, Terms()));
        await setter.HandleAsync(new SetBookingPolicyCommand(visible.Id, _ownerId, Terms()));
        var preview = new GetBookingPolicyPreviewHandler(db, new BookingPolicyProvider(db));

        (await preview.HandleAsync(new GetBookingPolicyPreviewQuery(hidden.Id))).Error.Code.Should().Be("Venue.NotFound");
        var shown = await preview.HandleAsync(new GetBookingPolicyPreviewQuery(visible.Id));
        shown.Value.DepositSummaryKey.Should().Be("deposit.Percentage");
        shown.Value.FreeCancellationUntilHoursBefore.Should().Be(24);
    }

    private async Task<Venue> SeedVenueAsync(VenuesDbContext db, bool approvedAndPublished)
    {
        var venue = Venue.Create(Guid.NewGuid(), "Club", $"club-{Guid.NewGuid():N}", null, _ownerId, "Addr", "Cairo", "Maadi", null, null, "+201000000001", null, null, _now);
        if (approvedAndPublished)
        {
            venue.SubmitForApproval(_ownerId, null, _now);
            venue.Approve(Guid.NewGuid(), null, _now);
            venue.SetPublished(true, _now);
        }

        db.Venues.Add(venue);
        await db.SaveChangesAsync();
        return venue;
    }

    private static VenuesDbContext NewDb() => new(new DbContextOptionsBuilder<VenuesDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private sealed class NoopAudit : IVenueAuditLogger
    {
        public Task LogAsync(string action, string entityName, Guid entityId, Guid userId, object? metadata = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class MutableClock : IClock
    {
        public DateTime UtcNow { get; set; } = _now;
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
