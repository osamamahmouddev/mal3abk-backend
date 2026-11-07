using FluentAssertions;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Bookings.CancelBooking;
using Malabak.Modules.Booking.Application.Disputes;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.Modules.Support.Domain;
using Malabak.Modules.Support.Infrastructure;
using Malabak.Modules.Teams.Application.Admin;
using Malabak.Modules.Teams.Domain;
using Malabak.Modules.Teams.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using BookingEntity = Malabak.Modules.Booking.Domain.Booking;

namespace Malabak.UnitTests;

/// <summary>Deeper edge cases for TASK-704 (disputes), TASK-705 (moderation), TASK-706 (support) and TASK-710 (KPIs).</summary>
public sealed class Phase6CommunityEdgeTests
{
    private static readonly DateTime _now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _player = Guid.NewGuid();
    private readonly Guid _venue = Guid.NewGuid();
    private readonly Recorder _recorder = new();
    private readonly Clock _clock = new();

    // ---------- TASK-704: disputes ----------

    [Fact]
    public async Task AResolvedDispute_TakesNoMoreEvidence_ButANewDisputeCanBeOpenedAsync()
    {
        await using var db = BookingDb();
        var booking = await SeedAsync(db, b => b.SubmitProof(Guid.NewGuid(), "p", _now));
        var service = Disputes(db);
        var first = (await service.OpenByPlayerAsync(booking.Id, _player, "Paid already")).Value;
        await service.ResolveAsync(first.Id, "Dismiss", "Duplicate of a support ticket", _admin);

        (await service.AddPlayerEvidenceAsync(booking.Id, _player, "more", null)).Error.Should().Be(DisputeErrors.NotFound, "no open dispute is left");
        (await service.AddAdminNoteAsync(first.Id, _admin, "late note")).Error.Should().Be(DisputeErrors.AlreadyResolved);
        (await service.StartReviewAsync(first.Id, _admin)).Error.Should().Be(DisputeErrors.AlreadyResolved);

        var second = await service.OpenByVenueAsync(booking.Id, _venue, Guid.NewGuid(), "Now the receipt looks edited");
        second.IsSuccess.Should().BeTrue("one *open* dispute per booking, not one ever");
        (await service.GetForPlayerAsync(booking.Id, _player)).Value.Id.Should().Be(second.Value.Id, "the latest dispute is shown");
    }

    [Fact]
    public async Task Dismiss_LeavesTheBookingUnchanged_AndSystemDisputesAreAuditedAsSystemAsync()
    {
        await using var db = BookingDb();
        var booking = await SeedAsync(db, b => b.SubmitProof(Guid.NewGuid(), "p", _now));
        var service = Disputes(db);
        var dispute = (await service.OpenBySystemAsync(booking.Id, "Same receipt used on two bookings")).Value;

        dispute.OpenedBy.Should().Be(nameof(DisputeOpenedBy.System));
        _recorder.Audits.Should().Contain(a => a.Action == "dispute.opened" && a.ActorType == AuditActorType.System && a.ActorId == null);

        var resolved = (await service.ResolveAsync(dispute.Id, "dismiss", "False alarm", _admin)).Value;
        resolved.BookingStatus.Should().Be(nameof(BookingStatus.ProofSubmitted));
        (await service.ResolveAsync(Guid.NewGuid(), "Dismiss", "x", _admin)).Error.Should().Be(DisputeErrors.NotFound);
    }

    [Theory]
    [InlineData("Nonsense")]
    [InlineData("")]
    [InlineData("99")]
    public async Task UnknownOutcomes_AreRejectedAsync(string outcome)
    {
        await using var db = BookingDb();
        var booking = await SeedAsync(db, b => b.SubmitProof(Guid.NewGuid(), "p", _now));
        var service = Disputes(db);
        var dispute = (await service.OpenByPlayerAsync(booking.Id, _player, "Paid")).Value;

        (await service.ResolveAsync(dispute.Id, outcome, "reason here", _admin)).Error.Should().Be(DisputeErrors.ReasonRequired);
        (await db.Bookings.SingleAsync()).Status.Should().Be(BookingStatus.ProofSubmitted);
    }

    [Fact]
    public async Task RefundPlayer_WithoutADeposit_IsNotApplicable_AndAlreadyPaidRefundsAreNeverChangedAsync()
    {
        await using var db = BookingDb();
        var free = await SeedAsync(db, b => b.SubmitProof(Guid.NewGuid(), "p", _now), deposit: 0);
        var service = Disputes(db);
        var d1 = (await service.OpenByPlayerAsync(free.Id, _player, "x")).Value;
        (await service.ResolveAsync(d1.Id, "RefundPlayer", "no deposit to refund", _admin)).Error.Should().Be(DisputeErrors.OutcomeNotApplicable);

        var paid = await SeedAsync(db, b =>
        {
            b.SubmitProof(Guid.NewGuid(), "p", _now);
            b.Confirm("s", _now);
            return b.Cancel("x", "p", isStaff: true, _now);
        });
        var refund = BookingRefund.CreatePending(Guid.NewGuid(), paid.Id, 100, "EGP", "cancel", null, _now);
        refund.RecordManualRefund(100, 100, "sent", "staff", _now);
        db.BookingRefunds.Add(refund);
        await db.SaveChangesAsync();
        var d2 = (await service.OpenByPlayerAsync(paid.Id, _player, "y")).Value;

        (await service.ResolveAsync(d2.Id, "RefundPlayer", "already refunded", _admin)).Error.Should().Be(BookingErrors.RefundAlreadyDecided);
        (await db.BookingRefunds.SingleAsync(r => r.BookingId == paid.Id)).Status.Should().Be(RefundStatus.Refunded);
    }

    [Fact]
    public async Task ReasonsAndEvidence_HaveLengthLimitsAsync()
    {
        await using var db = BookingDb();
        var booking = await SeedAsync(db, b => b.SubmitProof(Guid.NewGuid(), "p", _now));
        var service = Disputes(db);

        (await service.OpenByPlayerAsync(booking.Id, _player, new string('x', 2001))).Error.Should().Be(DisputeErrors.ReasonRequired);
        await service.OpenByPlayerAsync(booking.Id, _player, "ok");
        (await service.AddPlayerEvidenceAsync(booking.Id, _player, new string('x', 2001), null)).Error.Should().Be(DisputeErrors.InvalidEvidence);
        (await service.AddVenueEvidenceAsync(booking.Id, Guid.NewGuid(), Guid.NewGuid(), "wrong venue", null)).Error.Should().Be(DisputeErrors.NotFound);
    }

    // ---------- TASK-706: support ----------

    [Fact]
    public async Task AWaitingTicket_GoesBackToOpen_WhenTheRequesterReplies_AndClosedTicketsTakeNothingAsync()
    {
        await using var db = SupportDb();
        var service = Support(db);
        var id = (await service.OpenAsync(_player, TicketRequesterKind.Player, new OpenTicketInput("Booking", "Help", "Booking stuck", null, null))).Value.Ticket.Id;
        await service.ReplyAsAgentAsync(id, _admin, "Which booking?", isInternal: false);

        (await service.ReplyAsRequesterAsync(id, _player, "MAL-AB12CD")).Value.Ticket.Status.Should().Be(nameof(TicketStatus.Open), "unassigned, so it goes back to the queue");

        await service.CloseAsync(id, "Spam", _admin);
        (await service.ReplyAsAgentAsync(id, _admin, "hi", isInternal: true)).Error.Should().Be(SupportErrors.Closed);
        (await service.AssignAsync(id, _admin, _admin)).Error.Should().Be(SupportErrors.Closed);
        (await service.SetPriorityAsync(id, "High", _admin)).Error.Should().Be(SupportErrors.Closed);
        (await service.CloseAsync(id, "again", _admin)).Error.Should().Be(SupportErrors.InvalidTransition);
    }

    [Theory]
    [InlineData("Banana")]
    [InlineData("")]
    [InlineData(null)]
    public async Task UnknownPriorities_AreRejectedAsync(string? priority)
    {
        await using var db = SupportDb();
        var service = Support(db);
        var id = (await service.OpenAsync(_player, TicketRequesterKind.Player, new OpenTicketInput("Other", "x", "y", null, null))).Value.Ticket.Id;

        (await service.SetPriorityAsync(id, priority, _admin)).Error.Should().Be(SupportErrors.Invalid);
    }

    [Fact]
    public async Task TicketMessages_AreTrimmed_AndBoundedAsync()
    {
        await using var db = SupportDb();
        var service = Support(db);

        (await service.OpenAsync(_player, TicketRequesterKind.Player, new OpenTicketInput("Other", new string('s', 201), "body", null, null))).Error.Should().Be(SupportErrors.Invalid);
        (await service.OpenAsync(_player, TicketRequesterKind.Player, new OpenTicketInput("Other", "s", new string('b', 4001), null, null))).Error.Should().Be(SupportErrors.InvalidMessage);
        var ticket = (await service.OpenAsync(_player, TicketRequesterKind.Player, new OpenTicketInput("Other", "  Subject  ", "  Body  ", null, null))).Value;
        ticket.Ticket.Subject.Should().Be("Subject");
        ticket.Messages.Single().Body.Should().Be("Body");
        (await service.GetAsync(Guid.NewGuid())).Error.Should().Be(SupportErrors.NotFound);
    }

    [Fact]
    public async Task ResolvedTicketsFreeASlot_ForTheOpenTicketLimitAsync()
    {
        await using var db = SupportDb();
        var service = Support(db);
        var ids = new List<Guid>();
        for (var i = 0; i < SupportTicketService.MaxOpenTicketsPerRequester; i++)
        {
            ids.Add((await service.OpenAsync(_player, TicketRequesterKind.Player, new OpenTicketInput("Other", $"t{i}", "b", null, null))).Value.Ticket.Id);
        }

        await service.ResolveAsync(ids[0], "done", _admin);

        (await service.OpenAsync(_player, TicketRequesterKind.Player, new OpenTicketInput("Other", "new", "b", null, null))).IsSuccess.Should().BeTrue();
    }

    // ---------- TASK-705: moderation ----------

    [Fact]
    public async Task TeamSuspension_ValidatesInput_AndBlocksJoinRequestsAsync()
    {
        await using var db = TeamsDb();
        var captain = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Giza Hawks", "giza-hawks", "Giza", "Giza", captain, "HAWK01", _now).Value;
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        var service = new AdminTeamModerationService(db, _recorder, _recorder, _clock);

        (await service.SuspendAsync(Guid.NewGuid(), "x", _admin)).Error.Should().Be(TeamErrors.NotFound);
        (await service.SuspendAsync(team.Id, new string('x', 501), _admin)).IsFailure.Should().BeTrue();
        (await service.ReactivateAsync(team.Id, "not suspended", _admin)).Error.Code.Should().Be("Team.NotSuspended");
        await service.SuspendAsync(team.Id, "Match fixing", _admin);
        (await service.SuspendAsync(team.Id, "again", _admin)).Error.Code.Should().Be("Team.AlreadySuspended");

        var tracked = await db.Teams.Include(t => t.Members).SingleAsync();
        tracked.CreateJoinRequest(Guid.NewGuid(), "let me in", 0, _now).Error.Should().Be(TeamErrors.Suspended);
        tracked.UpdateSettings(false, isRecruiting: false, isOpenForChallenges: false, captain, _now).IsSuccess.Should().BeTrue("a suspended team may still close itself down");
        (await service.SearchAsync(null, suspended: true, 1, 25)).Value.Items.Should().ContainSingle(t => t.SuspensionReason == "Match fixing");
        (await service.SearchAsync(null, null, 0, 25)).IsFailure.Should().BeTrue();
    }

    // ---------- TASK-710: KPI providers ----------

    [Fact]
    public async Task SupportMetrics_CountOpenTicketsByCategory_AndOpenedInThePeriodAsync()
    {
        await using var db = SupportDb();
        var service = Support(db);
        var a = (await service.OpenAsync(_player, TicketRequesterKind.Player, new OpenTicketInput("Refund", "r", "b", null, null))).Value.Ticket.Id;
        await service.OpenAsync(Guid.NewGuid(), TicketRequesterKind.Venue, new OpenTicketInput("Refund", "r2", "b", null, null));
        await service.OpenAsync(Guid.NewGuid(), TicketRequesterKind.Player, new OpenTicketInput("Account", "a", "b", null, null));
        await service.ResolveAsync(a, "done", _admin);

        var part = await new SupportMetricsProvider(db).GetAsync(new MetricsPeriod(_now.AddDays(-1), _now.AddDays(1), _cairo));

        part.Figures["support.open"].Should().Be(2);
        part.Figures["support.opened"].Should().Be(3);
        part.Distributions["support.open_by_category"].Should().Equal(new Dictionary<string, int> { ["Refund"] = 1, ["Account"] = 1 });
    }

    [Fact]
    public void DailySeries_HandleASingleDay_AndWeightedValues()
    {
        var period = new MetricsPeriod(new DateTime(2026, 11, 6, 22, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 7, 22, 0, 0, DateTimeKind.Utc), _cairo);
        var at = new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc);

        var series = period.Daily([at, at.AddMinutes(1)], instant => instant == at ? 300m : 200m);

        series.Should().ContainSingle().Which.Should().Be(new SeriesPoint(new DateOnly(2026, 11, 7), 500m));
    }

    private PaymentDisputeService Disputes(BookingDbContext db) =>
        new(db, new VenueCancelBookingHandler(db, _clock, new NoEffects(), new NoEffects(), NullLogger<VenueCancelBookingHandler>.Instance),
            new NoEffects(), new NoEffects(), new NoVenues(), _recorder, _recorder, _clock);

    private SupportTicketService Support(SupportDbContext db) => new(db, _recorder, _recorder, _clock);

    private async Task<BookingEntity> SeedAsync(BookingDbContext db, Func<BookingEntity, Result> arrange, decimal deposit = 100)
    {
        var booking = BookingEntity.Create(Guid.NewGuid(), "MAL-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), _venue, Guid.NewGuid(), _player,
            _now.AddDays(3), _now.AddDays(3).AddHours(1), Money.From(400), Money.From(deposit), _now.AddMinutes(30), null, null, _now.AddDays(-1), "p").Value;
        arrange(booking).IsSuccess.Should().BeTrue();
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking;
    }

    private static BookingDbContext BookingDb() => new(new DbContextOptionsBuilder<BookingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SupportDbContext SupportDb() => new(new DbContextOptionsBuilder<SupportDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static TeamsDbContext TeamsDb() => new(new DbContextOptionsBuilder<TeamsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class NoVenues : IVenueDirectory
    {
        public Task<Guid?> GetVenueIdOfPitchAsync(Guid pitchId, CancellationToken cancellationToken = default) => Task.FromResult<Guid?>(null);

        public Task<VenueSummary?> GetVenueSummaryAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.FromResult<VenueSummary?>(null);

        public Task<IReadOnlyList<VenueBlockedPeriod>> GetBlockedPeriodsAsync(Guid venueId, DateTime fromUtc, DateTime toUtc, Guid? pitchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VenueBlockedPeriod>>([]);

        public Task<IReadOnlyDictionary<Guid, string>> GetVenueNamesAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyDictionary<Guid, string>> GetVenueCoverPhotosAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyDictionary<Guid, string>> GetPitchNamesAsync(IReadOnlyCollection<Guid> pitchIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyList<Guid>> GetStaffUserIdsAsync(Guid venueId, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
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
        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
