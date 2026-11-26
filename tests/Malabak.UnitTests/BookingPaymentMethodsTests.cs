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
    private readonly PlatformAccounts _payments = new();

    [Fact]
    public async Task ThePlayer_SeesWhereToPayWhileTheHoldIsOpenAsync()
    {
        await using var db = NewDb();
        var booking = Hold(minutesLeft: 10);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(new GetBookingPaymentMethodsQuery(booking.Id, _player));

        result.IsSuccess.Should().BeTrue();
        result.Value.Where(m => m.IsAvailable).Should().ContainSingle().Which.AccountDetails.Should().Be("01000000000");
        result.Value.Should().OnlyContain(m => m.RecipientName == "Malabak", "deposits go to the platform, never the venue");
        _payments.Reads.Should().BeGreaterThan(0);
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
        _payments.Reads.Should().Be(0, "the accounts aren't even looked up for someone else's booking");
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
        _payments.Reads.Should().Be(0);
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
        _payments.Reads.Should().Be(0);
    }

    private GetBookingPaymentMethodsHandler Handler(BookingDbContext db) => new(db, new FakeClock(_now), _payments);

    private static BookingDbContext NewDb() =>
        new(new DbContextOptionsBuilder<BookingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private BookingEntity Hold(int minutesLeft)
    {
        var created = _now.AddMinutes(minutesLeft - 15);
        return BookingEntity.Create(Guid.NewGuid(), $"MLB-{Guid.NewGuid():N}"[..12], _venue, Guid.NewGuid(), _player,
            _now.AddDays(1), _now.AddDays(1).AddHours(1), Money.From(400), Money.From(100), created.AddMinutes(15),
            null, null, created, "player").Value;
    }

    /// <summary>The platform's deposit accounts as admins set them: a wallet only, and the recipient name.</summary>
    private sealed class PlatformAccounts : IPlatformSettings
    {
        public int Reads { get; private set; }

        public Task<int> GetIntAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<bool> GetBoolAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<string> GetStringAsync(string key, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(key switch
            {
                PlatformSettingKeys.PlatformVodafoneCashNumber => "01000000000",
                PlatformSettingKeys.PlatformDepositRecipientName => "Malabak",
                _ => string.Empty,
            });
        }

        public Task<decimal> GetDecimalAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(0m);
    }
}
