using FluentAssertions;
using Malabak.Modules.Booking.Application.Bookings.PaymentDetails;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.Modules.Venues.Application.PaymentMethods;
using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Xunit;
using BookingEntity = Malabak.Modules.Booking.Domain.Booking;

namespace Malabak.UnitTests;

public sealed class PaymentMethodAndDisputeEdgeCaseTests
{
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc));

    private static BookingDbContext CreateBookingDb()
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new BookingDbContext(options);
    }

    [Theory]
    [InlineData("01012345678", true)]
    [InlineData("+201123456789", true)]
    [InlineData("01234567890", true)]
    [InlineData("01555555555", true)]
    [InlineData("01312345678", false)] // Invalid prefix 013
    [InlineData("01412345678", false)] // Invalid prefix 014
    [InlineData("0223456789", false)]  // Cairo landline
    [InlineData("+966501234567", false)] // Saudi mobile
    [InlineData("+12025550199", false)] // US number
    [InlineData("0101234567", false)]   // Too short (10 digits)
    [InlineData("010123456789", false)] // Too long (12 digits)
    [InlineData("010abc45678", false)]  // Letters
    public void PaymentMethodType_MobileWallet_ValidatesEgyptianMobileNumbers(string input, bool expectedValid)
    {
        var normalized = PaymentMethodType.Normalize(input);
        var isValid = PaymentMethodType.IsValidAccountDetails(PaymentMethodType.MobileWallet, normalized);
        isValid.Should().Be(expectedValid);
    }

    [Theory]
    [InlineData("EG123456789012345678901234567", true)] // EG + 27 digits = 29 chars
    [InlineData("EG999999999999999999999999999", true)]
    [InlineData("EG12345678901234567890123456", false)] // 26 digits (too short)
    [InlineData("EG1234567890123456789012345678", false)] // 28 digits (too long)
    [InlineData("SA123456789012345678901234567", false)] // Saudi IBAN
    [InlineData("12345678", true)]                       // 8-digit bank account
    [InlineData("12345678901234567890", true)]           // 20-digit bank account
    [InlineData("1234567", false)]                      // 7 digits (too short for account)
    [InlineData("123456789012345678901", false)]         // 21 digits (too long for account)
    public void PaymentMethodType_BankTransfer_ValidatesEgyptianIbanAndAccountNumbers(string input, bool expectedValid)
    {
        var normalized = PaymentMethodType.Normalize(input);
        var isValid = PaymentMethodType.IsValidAccountDetails(PaymentMethodType.BankTransfer, normalized);
        isValid.Should().Be(expectedValid);
    }

    [Theory]
    [InlineData("ahmed.ezz@instapay", true)]
    [InlineData("user_123@instapay", true)]
    [InlineData("CAPTAIN@INSTAPAY", true)]
    [InlineData("01012345678", true)] // Egyptian mobile also valid on InstaPay
    [InlineData("+201123456789", true)]
    [InlineData("ab@instapay", false)] // Too short (< 3 chars before @)
    [InlineData("ahmed@vodafone", false)] // Wrong domain
    [InlineData("ahmed.ezz", false)] // Missing @instapay
    [InlineData("ahmed#special@instapay", false)] // Invalid character
    public void PaymentMethodType_InstaPay_ValidatesAddressAndMobile(string input, bool expectedValid)
    {
        var normalized = PaymentMethodType.Normalize(input);
        var isValid = PaymentMethodType.IsValidAccountDetails(PaymentMethodType.InstaPay, normalized);
        isValid.Should().Be(expectedValid);
    }

    [Theory]
    [InlineData(DepositType.Percentage, 0)]
    [InlineData(DepositType.Percentage, -10)]
    [InlineData(DepositType.Percentage, 101)]
    [InlineData(DepositType.FixedAmount, 0)]
    [InlineData(DepositType.FixedAmount, -50)]
    [InlineData(DepositType.FixedAmount, 100001)] // Exceeds MaxFixedAmount (100,000)
    [InlineData(DepositType.FixedAmount, 100.555)] // More than 2 decimal places
    public void DepositRule_InvalidValues_ShouldThrowArgumentOutOfRangeException(DepositType type, decimal value)
    {
        var act = () => DepositRule.Create(type, value);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DepositRule_Full_ShouldNormalizeValueToZero()
    {
        var rule = DepositRule.Create(DepositType.Full, 500m);
        rule.Type.Should().Be(DepositType.Full);
        rule.Value.Should().Be(0m);
        rule.CalculateDeposit(bookingPrice: 400m).Should().Be(400m);
    }

    [Fact]
    public void DepositRule_Percentage_CalculatesAccurateProportion()
    {
        var rule = DepositRule.Create(DepositType.Percentage, 25m); // 25%
        rule.CalculateDeposit(bookingPrice: 600m).Should().Be(150m);
    }

    [Fact]
    public void PaymentMethodInput_Validate_WithInvalidAccountDetails_ShouldFail()
    {
        // Act: Invalid mobile number for MobileWallet
        var result = PaymentMethodInput.Validate(
            PaymentMethodType.MobileWallet,
            recipientName: "Ahmed Ali",
            accountDetails: "01312345678", // Invalid prefix 013
            instructions: "Send screenshot",
            depositType: DepositType.Full,
            depositValue: 0);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PaymentMethodErrors.InvalidAccountDetails);
    }

    [Fact]
    public void PaymentMethodInput_Validate_WithUnknownMethodType_ShouldFail()
    {
        // Act
        var result = PaymentMethodInput.Validate(
            "CryptoCurrency",
            recipientName: "Owner",
            accountDetails: "BTC1234567890",
            instructions: null,
            depositType: DepositType.Full,
            depositValue: 0);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PaymentMethodErrors.InvalidType);
    }

    [Fact]
    public async Task GetBookingPaymentMethods_ByStrangerPlayer_ShouldReturnNotFoundAsync()
    {
        // Arrange
        await using var db = CreateBookingDb();
        var legitimatePlayerId = Guid.NewGuid();
        var strangerPlayerId = Guid.NewGuid();
        var venueId = Guid.NewGuid();

        var created = _clock.UtcNow;
        var booking = BookingEntity.Create(
            Guid.NewGuid(), "MLB-TEST-0001", venueId, Guid.NewGuid(), legitimatePlayerId,
            _clock.UtcNow.AddDays(2), _clock.UtcNow.AddDays(2).AddHours(1),
            Money.From(300), Money.From(150), created.AddMinutes(10),
            null, null, created, "player").Value;

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var fakePayments = new FakeVenuePaymentDirectory();
        var handler = new GetBookingPaymentMethodsHandler(db, fakePayments, _clock);

        // Act: Stranger attempts to view payment credentials of another player's booking
        var query = new GetBookingPaymentMethodsQuery(booking.Id, strangerPlayerId);
        var result = await handler.HandleAsync(query);

        // Assert: Security invariant - prevents account number enumeration
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BookingErrors.NotFound);
    }

    [Fact]
    public async Task GetBookingPaymentMethods_WhenHoldExpired_ShouldReturnHoldExpiredAsync()
    {
        // Arrange
        await using var db = CreateBookingDb();
        var playerId = Guid.NewGuid();
        var venueId = Guid.NewGuid();

        var created = _clock.UtcNow.AddMinutes(-20);
        var booking = BookingEntity.Create(
            Guid.NewGuid(), "MLB-TEST-0002", venueId, Guid.NewGuid(), playerId,
            _clock.UtcNow.AddDays(2), _clock.UtcNow.AddDays(2).AddHours(1),
            Money.From(300), Money.From(150), created.AddMinutes(10), // expired 10m ago
            null, null, created, "player").Value;

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var fakePayments = new FakeVenuePaymentDirectory();
        var handler = new GetBookingPaymentMethodsHandler(db, fakePayments, _clock);

        // Act
        var query = new GetBookingPaymentMethodsQuery(booking.Id, playerId);
        var result = await handler.HandleAsync(query);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BookingErrors.HoldExpired);
    }

    private sealed class FakeVenuePaymentDirectory : IVenuePaymentDirectory
    {
        public Task<IReadOnlyList<PayableVenuePaymentMethod>> RevealForBookingAsync(
            Guid venueId, Guid bookingId, Guid playerId, CancellationToken cancellationToken = default)
        {
            var list = new List<PayableVenuePaymentMethod>
            {
                new(Guid.NewGuid(), "InstaPay", "Al Ahly SC", "ahly@instapay", "Write booking ref in notes")
            };
            return Task.FromResult<IReadOnlyList<PayableVenuePaymentMethod>>(list);
        }
    }
}
