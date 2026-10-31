using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Malabak.Modules.Venues.Application.Pricing;
using Malabak.Modules.Venues.Application.Quotes;
using Malabak.Modules.Venues.Application.SpecialSchedules;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class BookingQuoteTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    private readonly MutableClock _clock = new();
    private readonly Guid _pitchId = Guid.NewGuid();
    private readonly Guid _playerId = Guid.NewGuid();
    private readonly List<UpcomingBookingRef> _bookings = [];
    private readonly FakePolicies _policies = new();
    private Guid _venueId;

    private static DateTime Local(int hour) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 5, hour, 0, 0, DateTimeKind.Unspecified), _cairo);

    [Fact]
    public async Task Quote_ReturnsTotals_RemainingBalance_PolicySummary_AndTokenAsync()
    {
        await using var db = await SeedAsync();

        var quote = (await Service(db).QuoteAsync(_playerId, _pitchId, Local(19), Local(21))).Value;

        quote.Total.Should().Be(800m);
        quote.Deposit.Should().Be(240m, "30% deposit policy");
        quote.RemainingBalance.Should().Be(560m);
        quote.Fees.Should().Be(0m);
        quote.SubjectToConfirmation.Should().BeTrue();
        quote.PolicySummary.Should().Contain(l => l.Contains("24 hours", StringComparison.Ordinal));
        quote.QuoteToken.Should().Contain(".");
        quote.ExpiresAtUtc.Should().Be(_clock.UtcNow + BookingQuoteService.QuoteLifetime);
    }

    [Fact]
    public async Task HeldOrBlockedSlots_CannotBeQuotedAsync()
    {
        await using var db = await SeedAsync();
        _bookings.Add(new UpcomingBookingRef(Guid.NewGuid(), "MLB-1", _pitchId, Local(19), Local(20), "PendingDeposit", _clock.UtcNow.AddMinutes(5)));

        (await Service(db).QuoteAsync(_playerId, _pitchId, Local(19), Local(21))).Error.Should().Be(QuoteErrors.SlotUnavailable);
        (await Service(db).QuoteAsync(_playerId, _pitchId, Local(15), Local(16))).IsFailure.Should().BeTrue("the venue opens at 18:00");
    }

    [Fact]
    public async Task ValidToken_Validates_AndTamperedOrForeignTokensFailAsync()
    {
        await using var db = await SeedAsync();
        var service = Service(db);
        var token = (await service.QuoteAsync(_playerId, _pitchId, Local(19), Local(21))).Value.QuoteToken;

        (await service.ValidateAsync(token, _playerId, _pitchId, Local(19), Local(21))).Value.Total.Should().Be(800m);
        (await service.ValidateAsync(token, Guid.NewGuid(), _pitchId, Local(19), Local(21))).Error.Should().Be(QuoteErrors.InvalidToken);
        (await service.ValidateAsync(token, _playerId, _pitchId, Local(20), Local(22))).Error.Should().Be(QuoteErrors.InvalidToken);
        (await service.ValidateAsync(token[..^2] + "xx", _playerId, _pitchId, Local(19), Local(21))).Error.Should().Be(QuoteErrors.InvalidToken);
        (await service.ValidateAsync("garbage", _playerId, _pitchId, Local(19), Local(21))).Error.Should().Be(QuoteErrors.InvalidToken);
    }

    [Fact]
    public async Task ExpiredQuote_IsRejectedAsync()
    {
        await using var db = await SeedAsync();
        var service = Service(db);
        var token = (await service.QuoteAsync(_playerId, _pitchId, Local(19), Local(21))).Value.QuoteToken;

        _clock.UtcNow += BookingQuoteService.QuoteLifetime;

        (await service.ValidateAsync(token, _playerId, _pitchId, Local(19), Local(21))).Error.Should().Be(QuoteErrors.Expired);
    }

    [Fact]
    public async Task PriceOrPolicyChangedAfterQuote_RequiresANewQuoteAsync()
    {
        await using var db = await SeedAsync();
        var service = Service(db);
        var token = (await service.QuoteAsync(_playerId, _pitchId, Local(19), Local(21))).Value.QuoteToken;

        db.PriceRules.Add(PriceRule.Create(_venueId,
            new PriceRuleTerms(PriceRuleKind.Peak, PriceAdjustment.Percentage, null, "Peak", null, 25, null, new TimeOnly(18, 0), new TimeOnly(23, 0), null, null, null),
            _clock.UtcNow).Value);
        await db.SaveChangesAsync();
        (await service.ValidateAsync(token, _playerId, _pitchId, Local(19), Local(21))).Error.Should().Be(QuoteErrors.PriceChanged);

        await using var db2 = await SeedAsync();
        var service2 = Service(db2);
        var token2 = (await service2.QuoteAsync(_playerId, _pitchId, Local(19), Local(21))).Value.QuoteToken;
        _policies.Version = 2;
        (await service2.ValidateAsync(token2, _playerId, _pitchId, Local(19), Local(21))).Error.Should().Be(QuoteErrors.PriceChanged);
    }

    [Fact]
    public async Task SlotTakenAfterQuote_FailsValidationAsync()
    {
        await using var db = await SeedAsync();
        var service = Service(db);
        var token = (await service.QuoteAsync(_playerId, _pitchId, Local(19), Local(21))).Value.QuoteToken;

        _bookings.Add(new UpcomingBookingRef(Guid.NewGuid(), "MLB-2", _pitchId, Local(20), Local(21), "Confirmed"));

        (await service.ValidateAsync(token, _playerId, _pitchId, Local(19), Local(21))).Error.Should().Be(QuoteErrors.SlotUnavailable);
    }

    private BookingQuoteService Service(VenuesDbContext db)
    {
        var bookings = new FakeBookings(_bookings);
        return new BookingQuoteService(db, new PricingService(db, _clock), new PitchAvailabilityChecker(db, _clock), bookings, _policies, new TestSigner(), _clock);
    }

    private async Task<VenuesDbContext> SeedAsync()
    {
        var db = new VenuesDbContext(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _venueId = Guid.NewGuid();
        db.Pitches.Add(Pitch.Create(_pitchId, _venueId, "P1", SurfaceType.ArtificialTurfValue, 5, null, null, null, _clock.UtcNow));
        db.OperatingSchedules.Add(OperatingSchedule.Create(Guid.NewGuid(), _venueId, DayOfWeek.Thursday, new TimeOnly(18, 0), new TimeOnly(23, 0), 60, _clock.UtcNow));
        db.PriceRules.Add(PriceRule.Create(_venueId,
            new PriceRuleTerms(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, null, "Base", 400, null, null, null, null, null, null, null), _clock.UtcNow).Value);
        db.Policies.Add(VenuePolicy.CreateVersion(_venueId, null,
            new BookingPolicyTerms(DepositRule.Create(DepositType.Percentage, 30), 30, 60, 24, RefundEligibility.FullRefund, 100, NoShowRule.ForfeitDeposit, 15, false, 0, true, null),
            Guid.NewGuid(), _clock.UtcNow));
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class TestSigner : IQuoteTokenSigner
    {
        private static readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

        public string Protect(byte[] payload) => $"{Convert.ToBase64String(payload)}.{Convert.ToBase64String(HMACSHA256.HashData(_key, payload))}";

        public byte[]? Unprotect(string token)
        {
            var parts = token.Split('.');
            try
            {
                var payload = Convert.FromBase64String(parts[0]);
                return parts.Length == 2 && CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(parts[1]), HMACSHA256.HashData(_key, payload))
                    ? payload
                    : null;
            }
            catch (FormatException)
            {
                return Encoding.UTF8.GetBytes("not-json");
            }
        }
    }

    private sealed class FakePolicies : IBookingPolicyProvider
    {
        public int Version { get; set; } = 1;

        public Task<BookingPolicySnapshot?> GetCurrentSnapshotAsync(Guid venueId, CancellationToken cancellationToken = default) =>
            Task.FromResult<BookingPolicySnapshot?>(new BookingPolicySnapshot(
                Guid.NewGuid(), venueId, Version, "Percentage", 30, 30, 60, 24, "FullRefund", 100, "ForfeitDeposit", 15, false, 0, true, null, DateTime.UtcNow));
    }

    private sealed class FakeBookings(List<UpcomingBookingRef> items) : IUpcomingBookingsReader
    {
        public Task<IReadOnlyList<UpcomingBookingRef>> GetActiveAsync(
            IReadOnlyCollection<Guid> pitchIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UpcomingBookingRef>>(items.Where(b => pitchIds.Contains(b.PitchId) && b.EndUtc > fromUtc && b.StartUtc < toUtc).ToList());
    }

    private sealed class MutableClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
