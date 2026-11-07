using FluentAssertions;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Bookings.CancelBooking;
using Malabak.Modules.Booking.Application.Disputes;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using BookingEntity = Malabak.Modules.Booking.Domain.Booking;

namespace Malabak.UnitTests;

public sealed class PaymentDisputeTests
{
    private static readonly DateTime _now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _venue = Guid.NewGuid();
    private readonly Guid _player = Guid.NewGuid();
    private readonly Guid _staff = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Recorder _recorder = new();
    private readonly Clock _clock = new();

    [Fact]
    public async Task OpeningADispute_IsOwnerScoped_OnePerBooking_AndNotifiesBothSidesAsync()
    {
        await using var db = NewDb();
        var booking = await SeedAsync(db, b => b.SubmitProof(Guid.NewGuid(), "p", _now));
        var service = Service(db);

        (await service.OpenByPlayerAsync(booking.Id, Guid.NewGuid(), "not mine")).Error.Should().Be(BookingErrors.NotFound);
        (await service.OpenByVenueAsync(booking.Id, Guid.NewGuid(), _staff, "other venue")).Error.Should().Be(BookingErrors.NotFound);
        (await service.OpenByPlayerAsync(booking.Id, _player, " ")).Error.Should().Be(DisputeErrors.ReasonRequired);

        var opened = (await service.OpenByPlayerAsync(booking.Id, _player, "I paid by InstaPay, the venue ignores me")).Value;
        opened.OpenedBy.Should().Be(nameof(DisputeOpenedBy.Player));
        opened.Status.Should().Be(nameof(DisputeStatus.Open));
        (await service.OpenByVenueAsync(booking.Id, _venue, _staff, "duplicate")).Error.Should().Be(DisputeErrors.AlreadyOpen);

        _recorder.Notifications.Where(n => n.Type == NotificationTypes.PaymentDisputeOpened).Select(n => n.UserId).Should().BeEquivalentTo([_player, _staff]);
        (await service.GetForVenueAsync(booking.Id, _venue)).Value.Id.Should().Be(opened.Id);
    }

    [Fact]
    public async Task Evidence_MustBeANoteOrAProofOfTheSameBookingAsync()
    {
        await using var db = NewDb();
        var proofId = Guid.NewGuid();
        var booking = await SeedAsync(db, b => b.SubmitProof(proofId, "p", _now));
        db.PaymentProofs.Add(Proof(proofId, booking.Id));
        await db.SaveChangesAsync();
        var service = Service(db);
        await service.OpenByVenueAsync(booking.Id, _venue, _staff, "The screenshot looks edited");

        (await service.AddPlayerEvidenceAsync(booking.Id, _player, null, Guid.NewGuid())).Error.Should().Be(DisputeErrors.InvalidEvidence);
        (await service.AddPlayerEvidenceAsync(booking.Id, _player, "  ", null)).Error.Should().Be(DisputeErrors.InvalidEvidence);
        var withProof = (await service.AddPlayerEvidenceAsync(booking.Id, _player, "Original receipt", proofId)).Value;
        var withNote = (await service.AddVenueEvidenceAsync(booking.Id, _venue, _staff, "Bank shows nothing on that date", null)).Value;

        withNote.Evidence.Should().HaveCount(2);
        withProof.Evidence.Should().ContainSingle(e => e.ProofId == proofId && e.Side == nameof(DisputeOpenedBy.Player));
    }

    [Fact]
    public async Task ConfirmBooking_ConfirmsAPendingProof_AndIsAuditedAsync()
    {
        await using var db = NewDb();
        var booking = await SeedAsync(db, b => b.SubmitProof(Guid.NewGuid(), "p", _now));
        var service = Service(db);
        var dispute = (await service.OpenByPlayerAsync(booking.Id, _player, "Paid")).Value;

        (await service.ResolveAsync(dispute.Id, "ConfirmBooking", "", _admin)).Error.Should().Be(DisputeErrors.ReasonRequired);
        var resolved = (await service.ResolveAsync(dispute.Id, "confirmbooking", "Bank statement matches the proof", _admin)).Value;

        resolved.Outcome.Should().Be(nameof(DisputeOutcome.ConfirmBooking));
        resolved.BookingStatus.Should().Be(nameof(BookingStatus.Confirmed));
        _recorder.Audits.Should().ContainSingle(a => a.Action == "admin.dispute.resolved" && a.ActorId == _admin);
        _recorder.Notifications.Count(n => n.Type == NotificationTypes.PaymentDisputeResolved).Should().Be(2);
        (await service.ResolveAsync(dispute.Id, "Dismiss", "again", _admin)).Error.Should().Be(DisputeErrors.AlreadyResolved);
    }

    [Fact]
    public async Task RefundPlayer_CancelsAnActiveBooking_AndOwesTheFullDepositAsync()
    {
        await using var db = NewDb();
        var booking = await SeedAsync(db, b =>
        {
            b.SubmitProof(Guid.NewGuid(), "p", _now);
            return b.Confirm("s", _now);
        }, deposit: 200);
        var service = Service(db);
        var dispute = (await service.OpenByPlayerAsync(booking.Id, _player, "Venue double-booked the slot")).Value;

        var resolved = (await service.ResolveAsync(dispute.Id, "RefundPlayer", "Venue admitted the double booking", _admin)).Value;

        resolved.BookingStatus.Should().Be(nameof(BookingStatus.Cancelled));
        (await db.BookingRefunds.SingleAsync()).Should().Match<BookingRefund>(r => r.Status == RefundStatus.Pending && r.ExpectedRefundAmount == 200);
    }

    [Fact]
    public async Task RefundPlayer_OnAClosedBooking_ReopensADeniedRefundAsync()
    {
        await using var db = NewDb();
        var booking = await SeedAsync(db, b =>
        {
            b.SubmitProof(Guid.NewGuid(), "p", _now);
            b.Confirm("s", _now);
            return b.Cancel("player cancelled", "p", isStaff: true, _now);
        }, deposit: 150);
        var refund = BookingRefund.CreatePending(Guid.NewGuid(), booking.Id, 150, "EGP", "cancel", null, _now);
        refund.DenyRefund("Too late", "staff", _now);
        db.BookingRefunds.Add(refund);
        await db.SaveChangesAsync();
        var service = Service(db);
        var dispute = (await service.OpenByPlayerAsync(booking.Id, _player, "Cancelled 3 days before, refund denied")).Value;

        (await service.ResolveAsync(dispute.Id, "ConfirmBooking", "x", _admin)).Error.Should().Be(DisputeErrors.OutcomeNotApplicable);
        (await service.ResolveAsync(dispute.Id, "RefundPlayer", "Policy allowed a refund", _admin)).IsSuccess.Should().BeTrue();

        (await db.BookingRefunds.SingleAsync()).Should().Match<BookingRefund>(r => r.Status == RefundStatus.Pending && r.ExpectedRefundAmount == 150);
    }

    [Fact]
    public async Task FavorVenue_RejectsAPendingProof_AndTheQueueHidesResolvedDisputesAsync()
    {
        await using var db = NewDb();
        var booking = await SeedAsync(db, b => b.SubmitProof(Guid.NewGuid(), "p", _now));
        var service = Service(db);
        var dispute = (await service.OpenByVenueAsync(booking.Id, _venue, _staff, "Fake receipt")).Value;
        (await service.StartReviewAsync(dispute.Id, _admin)).Value.Status.Should().Be(nameof(DisputeStatus.UnderReview));
        (await service.QueueAsync(null, null, 1, 25)).Value.Total.Should().Be(1);

        var resolved = (await service.ResolveAsync(dispute.Id, "FavorVenue", "Receipt reference does not exist", _admin)).Value;

        resolved.BookingStatus.Should().Be(nameof(BookingStatus.Rejected));
        (await service.QueueAsync(null, null, 1, 25)).Value.Total.Should().Be(0);
        (await service.QueueAsync("resolved", _venue, 1, 25)).Value.Total.Should().Be(1);
        (await service.QueueAsync("Banana", null, 1, 25)).Error.Should().Be(DisputeErrors.InvalidFilter);
    }

    [Fact]
    public async Task ManualBookings_CannotBeDisputedAsync()
    {
        await using var db = NewDb();
        var manual = BookingEntity.CreateManual(Guid.NewGuid(), "W-1", _venue, Guid.NewGuid(), _staff, "Walk-in", "+201000000000",
            _now.AddDays(1), _now.AddDays(1).AddHours(1), Money.From(300), Money.From(0), confirmedImmediately: true, null, _now).Value;
        db.Bookings.Add(manual);
        await db.SaveChangesAsync();

        (await Service(db).OpenBySystemAsync(manual.Id, "auto check")).Error.Should().Be(DisputeErrors.NotDisputable);
    }

    private PaymentDisputeService Service(BookingDbContext db) =>
        new(db, new VenueCancelBookingHandler(db, _clock, new NoEffects(), new NoEffects(), NullLogger<VenueCancelBookingHandler>.Instance),
            new NoEffects(), new NoEffects(), new StaffDirectory(_staff), _recorder, _recorder, _clock);

    private async Task<BookingEntity> SeedAsync(BookingDbContext db, Func<BookingEntity, Result> arrange, decimal deposit = 100)
    {
        var booking = BookingEntity.Create(Guid.NewGuid(), "MAL-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), _venue, Guid.NewGuid(), _player,
            _now.AddDays(3), _now.AddDays(3).AddHours(1), Money.From(400), Money.From(deposit), _now.AddMinutes(30), null, null, _now.AddDays(-1), "p").Value;
        arrange(booking).IsSuccess.Should().BeTrue();
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking;
    }

    private static PaymentProof Proof(Guid id, Guid bookingId) =>
        PaymentProof.Create(id, bookingId, null, "proofs/key.jpg", "receipt.jpg", "image/jpeg", 2048, new string('a', 64), "p", _now);

    private static BookingDbContext NewDb() => new(new DbContextOptionsBuilder<BookingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class StaffDirectory(Guid staff) : IVenueDirectory
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
            Task.FromResult<IReadOnlyList<Guid>>([staff]);
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
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
