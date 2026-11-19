using FluentAssertions;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Admin;
using Malabak.Modules.Booking.Application.Bookings.CancelBooking;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using BookingEntity = Malabak.Modules.Booking.Domain.Booking;

namespace Malabak.UnitTests;

public sealed class AdminFinancialSummaryTests
{
    private static readonly DateTime _now = new(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);
    private readonly Guid _venue = Guid.NewGuid();

    [Fact]
    public async Task TheVenueTotals_Reconcile_AndManualOrUnpaidBookingsDoNotCountAsCommissionOrCollectedAsync()
    {
        await using var db = new BookingDbContext(new DbContextOptionsBuilder<BookingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        // A: 400, deposit 100 sent to the venue's wallet, proof approved. Commission falls back to 10% = 40, still owed.
        var proofPaid = Online("MLB-A", _now.AddDays(-3));
        proofPaid.SubmitProof(Guid.NewGuid(), "p", _now).IsSuccess.Should().BeTrue();
        proofPaid.Confirm("staff", _now).IsSuccess.Should().BeTrue();

        // B: 250 walk-in the venue entered itself: no commission.
        var manual = BookingEntity.CreateManual(Guid.NewGuid(), "MLB-B", _venue, Guid.NewGuid(), Guid.NewGuid(), "Walk-in", "+201000000000",
            _now.AddDays(-2), _now.AddDays(-2).AddHours(1), Money.From(250), Money.From(0), true, null, _now).Value;

        // C: still waiting for its deposit: nothing is collected yet.
        var waiting = Online("MLB-C", _now.AddDays(-1));

        // D: 400, deposit 100 paid online to the platform: the 40 commission is kept, 60 is owed to the venue.
        var online = Online("MLB-D", _now.AddDays(-1).AddHours(3));
        online.ConfirmOnlinePayment("order", "txn", Money.From(100), _now).IsSuccess.Should().BeTrue();

        db.Bookings.AddRange(proofPaid, manual, waiting, online);
        await db.SaveChangesAsync();

        var row = (await Service(db).GetVenuesFinancialSummaryAsync(_now.AddDays(-7), _now)).Value.Single();

        row.TotalBookings.Should().Be(4);
        row.ConfirmedBookings.Should().Be(3);
        row.PendingConfirmationBookings.Should().Be(1);
        row.ManualBookings.Should().Be(1);
        row.TotalGrossAmount.Should().Be(1050);
        row.TotalDepositAmount.Should().Be(200, "the waiting booking's deposit was never paid");
        row.TotalPlatformCommission.Should().Be(80, "the manual booking carries no commission");
        row.TotalVenueNetAmount.Should().Be(970);
        row.TotalDueAtVenueCash.Should().Be(850);
        row.PlatformCollectedOnline.Should().Be(100);
        row.CommissionCollected.Should().Be(40);
        row.CommissionOutstanding.Should().Be(40);
        row.PlatformOwesVenue.Should().Be(60);
        row.VenueDepositShare.Should().Be(120, "deposits 200 minus commission 80");
        (row.VenueDepositShare + row.TotalDueAtVenueCash).Should().Be(row.TotalVenueNetAmount);

        // What the venue ends up with = its own deposits + cash at the venue + what the platform pays it - commission it still owes.
        var venueDeposits = row.TotalDepositAmount - row.PlatformCollectedOnline;
        (venueDeposits + row.TotalDueAtVenueCash + row.PlatformOwesVenue - row.CommissionOutstanding).Should().Be(row.TotalVenueNetAmount);
    }

    private BookingEntity Online(string reference, DateTime start) =>
        BookingEntity.Create(Guid.NewGuid(), reference, _venue, Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(1), Money.From(400), Money.From(100),
            _now.AddMinutes(30), null, null, _now.AddDays(-5), "p").Value;

    private static AdminBookingService Service(BookingDbContext db) =>
        new(db, new VenueCancelBookingHandler(db, new Clock(), new NoEffects(), new NoEffects(), NullLogger<VenueCancelBookingHandler>.Instance), new NoAudit(), new Clock());

    private sealed class NoAudit : IAuditLog
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
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

    private sealed class Clock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
