using FluentAssertions;
using Malabak.Modules.Booking.Application;
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

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// The commission cycle against a real PostgreSQL with the real migrations: the player's receipt is approved by the platform
/// team, the platform holds the deposit, keeps the commission fixed in the quote and owes the venue the rest, and payouts can
/// never add up to more than that, even when two admins record one at the same moment.
/// </summary>
public sealed class CommissionSettlementTests : IClassFixture<BookingDatabaseFixture>
{
    private static readonly DateTime _start = new(DateTime.UtcNow.Year + 5, 9, 1, 18, 0, 0, DateTimeKind.Utc);

    private readonly BookingDatabaseFixture _database;
    private readonly Guid _admin = Guid.NewGuid();

    public CommissionSettlementTests(BookingDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task ApprovedDeposit_IsOwedToTheVenueLessTheCommission_AndPaidOutOnceAsync()
    {
        var venueId = Guid.NewGuid();
        var bookingId = await SaveProofSubmittedAsync(venueId, deposit: 150, commission: 60, hourOffset: 0);

        await using (var db = _database.CreateContext())
        {
            (await Admin(db).ReviewDepositAsync(bookingId, approved: true, rejectionReason: null, _admin)).IsSuccess.Should().BeTrue();
        }

        await using (var db = _database.CreateContext())
        {
            var booking = await db.Bookings.SingleAsync(b => b.Id == bookingId);
            booking.Status.Should().Be(BookingStatus.Confirmed);
            booking.OnlinePaidAmount.Amount.Should().Be(150, "the deposit reached the platform's account");

            var statement = await Payouts(db).GetStatementAsync(venueId);
            statement.Value.Balance.VenueShare.Should().Be(90);
            statement.Value.Balance.Balance.Should().Be(90);

            var paid = await Payouts(db).RecordAsync(venueId, new RecordVenuePayoutInput(90, PayoutMethods.InstaPay, "IP-1001", null, null), _admin);
            paid.IsSuccess.Should().BeTrue();
        }

        await using (var db = _database.CreateContext())
        {
            (await Payouts(db).GetStatementAsync(venueId)).Value.Balance.Balance.Should().Be(0);
            var again = await Payouts(db).RecordAsync(venueId, new RecordVenuePayoutInput(1, PayoutMethods.InstaPay, "IP-1002", null, null), _admin);
            again.Error.Should().Be(VenuePayoutErrors.ExceedsBalance);
        }
    }

    [Fact]
    public async Task TwoAdminsPayingAtOnce_CannotPayMoreThanIsOwedAsync()
    {
        var venueId = Guid.NewGuid();
        var bookingId = await SaveProofSubmittedAsync(venueId, deposit: 200, commission: 50, hourOffset: 2);
        await using (var db = _database.CreateContext())
        {
            (await Admin(db).ReviewDepositAsync(bookingId, true, null, _admin)).IsSuccess.Should().BeTrue();
        }

        // 150 is owed; each admin tries to send 100 at the same moment.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
        {
            await using var db = _database.CreateContext();
            return await Payouts(db).RecordAsync(venueId, new RecordVenuePayoutInput(100, PayoutMethods.Wallet, $"W-{i}", null, null), Guid.NewGuid());
        }));

        attempts.Count(a => a.IsSuccess).Should().Be(1);
        attempts.Single(a => a.IsFailure).Error.Should().Be(VenuePayoutErrors.ExceedsBalance);
        await using var verify = _database.CreateContext();
        (await verify.VenuePayouts.Where(p => p.VenueId == venueId).SumAsync(p => p.Amount)).Should().Be(100);
    }

    private async Task<Guid> SaveProofSubmittedAsync(Guid venueId, decimal deposit, decimal commission, int hourOffset)
    {
        var start = _start.AddHours(hourOffset);
        var now = DateTime.UtcNow;
        var pitchId = Guid.NewGuid();
        var quote = new PriceQuoteSnapshot(venueId, pitchId, start, start.AddHours(1), 60, "EGP", 400, 0, null, 400, deposit, "Percentage", [], now,
            PlatformCommission: commission);
        var booking = BookingEntity.Create(Guid.NewGuid(), $"MAL-{Guid.NewGuid():N}"[..12].ToUpperInvariant(), venueId, pitchId, Guid.NewGuid(), start,
            start.AddHours(1), Money.From(400), Money.From(deposit), now.AddMinutes(30), quote, null, now, "player").Value;
        booking.SubmitProof(Guid.NewGuid(), "player", now).IsSuccess.Should().BeTrue();

        await using var db = _database.CreateContext();
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private static AdminBookingService Admin(BookingDbContext db)
    {
        var clock = new SystemClock();
        return new AdminBookingService(db,
            new VenueCancelBookingHandler(db, clock, new NoEffects(), new NoEffects(), NullLogger<VenueCancelBookingHandler>.Instance),
            new NoAudit(), clock, new NoEffects(), new NoEffects(), new FakeVenueDirectory());
    }

    private static VenuePayoutService Payouts(BookingDbContext db) =>
        new(db, new NoAudit(), new SystemClock(), new FakeVenueDirectory());

    private sealed class NoAudit : IAuditLog
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoEffects : IAvailabilityInvalidator, IBookingRealtimeBroadcaster
    {
        public Task InvalidatePitchAsync(Guid pitchId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task InvalidateVenueAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BroadcastSlotStatusChangedAsync(Guid venueId, Guid pitchId, DateTime startUtc, DateTime endUtc, string status,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
