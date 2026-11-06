using FluentAssertions;
using Malabak.Modules.Booking.Application.Bookings.PaymentDetails;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Xunit;
using BookingEntity = Malabak.Modules.Booking.Domain.Booking;

namespace Malabak.UnitTests;

public sealed class BookingPaymentMethodsTests
{
    private static readonly DateTime _now = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
    private readonly Guid _player = Guid.NewGuid();
    private readonly Guid _venue = Guid.NewGuid();
    private readonly RecordingPayments _payments = new();

    [Fact]
    public async Task ThePlayer_SeesWhereToPayWhileTheHoldIsOpenAsync()
    {
        await using var db = NewDb();
        var booking = Hold(minutesLeft: 10);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(new GetBookingPaymentMethodsQuery(booking.Id, _player));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.AccountDetails.Should().Be("01000000000");
        _payments.Reveals.Should().ContainSingle().Which.Should().Be((_venue, booking.Id, _player));
    }

    [Fact]
    public async Task AnotherPlayer_CannotTellTheBookingExistsAsync()
    {
        await using var db = NewDb();
        var booking = Hold(minutesLeft: 10);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(new GetBookingPaymentMethodsQuery(booking.Id, Guid.NewGuid()));

        result.Error.Should().Be(BookingErrors.NotFound);
        _payments.Reveals.Should().BeEmpty("nothing may be decrypted for someone else's booking");
    }

    [Fact]
    public async Task NothingIsRevealedOnceTheHoldEndedAsync()
    {
        await using var db = NewDb();
        var booking = Hold(minutesLeft: -1);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(new GetBookingPaymentMethodsQuery(booking.Id, _player));

        result.Error.Should().Be(BookingErrors.HoldExpired);
        _payments.Reveals.Should().BeEmpty();
    }

    [Fact]
    public async Task NothingIsRevealedAfterTheProofWasSentAsync()
    {
        await using var db = NewDb();
        var booking = Hold(minutesLeft: 10);
        booking.SubmitProof(Guid.NewGuid(), "player", _now).IsSuccess.Should().BeTrue();
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(new GetBookingPaymentMethodsQuery(booking.Id, _player));

        result.IsFailure.Should().BeTrue();
        _payments.Reveals.Should().BeEmpty();
    }

    private GetBookingPaymentMethodsHandler Handler(BookingDbContext db) => new(db, _payments, new FakeClock(_now));

    private static BookingDbContext NewDb() =>
        new(new DbContextOptionsBuilder<BookingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private BookingEntity Hold(int minutesLeft)
    {
        var created = _now.AddMinutes(minutesLeft - 15);
        return BookingEntity.Create(Guid.NewGuid(), $"MLB-{Guid.NewGuid():N}"[..12], _venue, Guid.NewGuid(), _player,
            _now.AddDays(1), _now.AddDays(1).AddHours(1), Money.From(400), Money.From(100), created.AddMinutes(15),
            null, null, created, "player").Value;
    }

    private sealed class RecordingPayments : IVenuePaymentDirectory
    {
        public List<(Guid VenueId, Guid BookingId, Guid PlayerId)> Reveals { get; } = [];

        public Task<IReadOnlyList<PayableVenuePaymentMethod>> RevealForBookingAsync(
            Guid venueId, Guid bookingId, Guid playerId, CancellationToken cancellationToken = default)
        {
            Reveals.Add((venueId, bookingId, playerId));
            return Task.FromResult<IReadOnlyList<PayableVenuePaymentMethod>>(
                [new PayableVenuePaymentMethod(Guid.NewGuid(), "MobileWallet", "Venue Owner", "01000000000", null)]);
        }
    }
}
