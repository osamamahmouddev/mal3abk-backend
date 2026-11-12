using FluentAssertions;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Bookings.CreateBooking;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// Phase 2 (P2) Edge-Case Tests: Booking hold creation, player pending hold quota (SECURITY.md §11),
/// venue hold window policy, overlap constraints, and expired hold penalties (ADR-0008 §1).
/// </summary>
public sealed class BookingHoldAndQuotaEdgeCaseTests : IClassFixture<BookingDatabaseFixture>
{
    private readonly BookingDatabaseFixture _database;
    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();
    private readonly DateTime _nowUtc = new(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc);

    public BookingHoldAndQuotaEdgeCaseTests(BookingDatabaseFixture database)
    {
        _database = database;
    }

    private static CreateBookingHandler CreateHandler(
        BookingDbContext db,
        FakeClock clock,
        PriceQuoteSnapshot quote,
        BookingPolicySnapshot? policy = null)
    {
        return new CreateBookingHandler(
            db,
            clock,
            new StubQuoteValidator(quote),
            new StubPolicyProvider(policy),
            new BookingReferenceGenerator(),
            new NoOpAvailabilityInvalidator(),
            new NoOpHoldExpiryScheduler(),
            new NoOpRealtimeBroadcaster(),
            NullLogger<CreateBookingHandler>.Instance);
    }

    private PriceQuoteSnapshot CreateQuoteSnapshot(DateTime startUtc, DateTime endUtc, decimal total = 500m, decimal deposit = 100m)
    {
        return new PriceQuoteSnapshot(
            _venueId,
            _pitchId,
            startUtc,
            endUtc,
            (int)(endUtc - startUtc).TotalMinutes,
            "EGP",
            total,
            0m,
            null,
            total,
            deposit,
            "Fixed",
            [new PriceLineSnapshot(startUtc, endUtc, total, total, [])],
            _nowUtc);
    }

    private BookingPolicySnapshot CreatePolicySnapshot(int proofWindowMinutes)
    {
        return new BookingPolicySnapshot(
            Guid.NewGuid(),
            _venueId,
            1,
            "Fixed",
            100m,
            proofWindowMinutes,
            120,
            24,
            "FullRefund",
            100m,
            "ForfeitDeposit",
            15,
            true,
            24,
            true,
            "Venue Policy",
            _nowUtc);
    }

    // =========================================================================
    // 1. Hold Length & Expiry Timestamp Precision
    // =========================================================================

    [Fact]
    public async Task HoldDuration_UsesVenuePolicyProofSubmissionWindowAsync()
    {
        var clock = new FakeClock(_nowUtc);
        await using var db = _database.CreateContext(clock);

        var startUtc = _nowUtc.AddHours(2);
        var endUtc = _nowUtc.AddHours(3);
        var quote = CreateQuoteSnapshot(startUtc, endUtc);
        var policy = CreatePolicySnapshot(proofWindowMinutes: 25);
        var handler = CreateHandler(db, clock, quote, policy);

        var result = await handler.HandleAsync(new CreateBookingCommand(
            _pitchId, Guid.NewGuid(), startUtc, endUtc, "quote-tok", 500m, "player-1"));

        result.IsSuccess.Should().BeTrue();
        result.Value.HoldExpiresAtUtc.Should().Be(_nowUtc.AddMinutes(25), "hold expiration must exactly match venue policy window");
    }

    [Fact]
    public async Task HoldDuration_WhenPolicyNull_FallsBackToPlatformDefault15MinutesAsync()
    {
        var clock = new FakeClock(_nowUtc);
        await using var db = _database.CreateContext(clock);

        var startUtc = _nowUtc.AddHours(2);
        var endUtc = _nowUtc.AddHours(3);
        var quote = CreateQuoteSnapshot(startUtc, endUtc);
        var handler = CreateHandler(db, clock, quote, policy: null);

        var result = await handler.HandleAsync(new CreateBookingCommand(
            _pitchId, Guid.NewGuid(), startUtc, endUtc, "quote-tok", 500m, "player-1"));

        result.IsSuccess.Should().BeTrue();
        result.Value.HoldExpiresAtUtc.Should().Be(_nowUtc.AddMinutes(15), "default platform hold duration is 15 minutes");
    }

    // =========================================================================
    // 2. Max 2 Pending Holds Per Player (Slot Hoarding Defense)
    // =========================================================================

    [Fact]
    public async Task PlayerQuota_Max2PendingHolds_ThirdHoldReturns409ConflictAsync()
    {
        var clock = new FakeClock(_nowUtc);
        var playerId = Guid.NewGuid();

        // 1st hold on pitch 1
        var pitch1 = Guid.NewGuid();
        var start1 = _nowUtc.AddHours(2);
        var end1 = _nowUtc.AddHours(3);
        var quote1 = new PriceQuoteSnapshot(_venueId, pitch1, start1, end1, 60, "EGP", 500m, 0m, null, 500m, 100m, "Fixed", [new PriceLineSnapshot(start1, end1, 500m, 500m, [])], _nowUtc);

        await using (var db1 = _database.CreateContext(clock))
        {
            var handler1 = CreateHandler(db1, clock, quote1);
            var res1 = await handler1.HandleAsync(new CreateBookingCommand(pitch1, playerId, start1, end1, "quote-1", 500m, playerId.ToString()));
            res1.IsSuccess.Should().BeTrue();
        }

        // 2nd hold on pitch 2
        var pitch2 = Guid.NewGuid();
        var start2 = _nowUtc.AddHours(4);
        var end2 = _nowUtc.AddHours(5);
        var quote2 = new PriceQuoteSnapshot(_venueId, pitch2, start2, end2, 60, "EGP", 500m, 0m, null, 500m, 100m, "Fixed", [new PriceLineSnapshot(start2, end2, 500m, 500m, [])], _nowUtc);

        await using (var db2 = _database.CreateContext(clock))
        {
            var handler2 = CreateHandler(db2, clock, quote2);
            var res2 = await handler2.HandleAsync(new CreateBookingCommand(pitch2, playerId, start2, end2, "quote-2", 500m, playerId.ToString()));
            res2.IsSuccess.Should().BeTrue();
        }

        // 3rd hold on pitch 3: Must be rejected with MaxPendingBookingsExceeded
        var pitch3 = Guid.NewGuid();
        var start3 = _nowUtc.AddHours(6);
        var end3 = _nowUtc.AddHours(7);
        var quote3 = new PriceQuoteSnapshot(_venueId, pitch3, start3, end3, 60, "EGP", 500m, 0m, null, 500m, 100m, "Fixed", [new PriceLineSnapshot(start3, end3, 500m, 500m, [])], _nowUtc);

        await using (var db3 = _database.CreateContext(clock))
        {
            var handler3 = CreateHandler(db3, clock, quote3);
            var res3 = await handler3.HandleAsync(new CreateBookingCommand(pitch3, playerId, start3, end3, "quote-3", 500m, playerId.ToString()));
            res3.IsFailure.Should().BeTrue();
            res3.Error.Code.Should().Be(BookingErrors.MaxPendingBookingsExceeded.Code);
        }

        // Advance clock 16 minutes so holds expire:
        clock.Advance(TimeSpan.FromMinutes(16));

        // Now that prior holds lapsed, a new hold must succeed!
        await using (var db4 = _database.CreateContext(clock))
        {
            var handler4 = CreateHandler(db4, clock, quote3);
            var res4 = await handler4.HandleAsync(new CreateBookingCommand(pitch3, playerId, start3, end3, "quote-3", 500m, playerId.ToString()));
            res4.IsSuccess.Should().BeTrue("expired holds must automatically release player quota");
        }
    }

    // =========================================================================
    // 3. Price Tamper Resistance
    // =========================================================================

    [Fact]
    public async Task CreateBooking_WhenClientSubmitsTamperedExpectedPrice_ReturnsPriceMismatchAsync()
    {
        var clock = new FakeClock(_nowUtc);
        await using var db = _database.CreateContext(clock);

        var startUtc = _nowUtc.AddHours(2);
        var endUtc = _nowUtc.AddHours(3);
        // Server quote is 500 EGP
        var quote = CreateQuoteSnapshot(startUtc, endUtc, total: 500m);
        var handler = CreateHandler(db, clock, quote);

        // Client attempts to pay 100 EGP instead of 500 EGP
        var result = await handler.HandleAsync(new CreateBookingCommand(
            _pitchId, Guid.NewGuid(), startUtc, endUtc, "quote-tok", ExpectedPrice: 100m, "player-1"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Booking.PriceMismatch");
    }

    // =========================================================================
    // 4. Overlap & Adjacency Constraints
    // =========================================================================

    [Fact]
    public async Task SlotOverlap_PartialOverlapRejected_AdjacentSlotsAllowedAsync()
    {
        var clock = new FakeClock(_nowUtc);
        var pitch = Guid.NewGuid();

        var slot1Start = _nowUtc.AddHours(2);
        var slot1End = _nowUtc.AddHours(3); // 16:00 - 17:00
        var quote1 = new PriceQuoteSnapshot(_venueId, pitch, slot1Start, slot1End, 60, "EGP", 500m, 0m, null, 500m, 100m, "Fixed", [new PriceLineSnapshot(slot1Start, slot1End, 500m, 500m, [])], _nowUtc);

        // Book slot 1: 16:00 - 17:00
        await using (var db1 = _database.CreateContext(clock))
        {
            var handler1 = CreateHandler(db1, clock, quote1);
            var res1 = await handler1.HandleAsync(new CreateBookingCommand(pitch, Guid.NewGuid(), slot1Start, slot1End, "tok-1", 500m, "p1"));
            res1.IsSuccess.Should().BeTrue();
        }

        // Partial overlap: 16:30 - 17:30 -> REJECTED
        var overlapStart = _nowUtc.AddHours(2).AddMinutes(30);
        var overlapEnd = overlapStart.AddHours(1);
        var quoteOverlap = new PriceQuoteSnapshot(_venueId, pitch, overlapStart, overlapEnd, 60, "EGP", 500m, 0m, null, 500m, 100m, "Fixed", [new PriceLineSnapshot(overlapStart, overlapEnd, 500m, 500m, [])], _nowUtc);

        await using (var db2 = _database.CreateContext(clock))
        {
            var handler2 = CreateHandler(db2, clock, quoteOverlap);
            var res2 = await handler2.HandleAsync(new CreateBookingCommand(pitch, Guid.NewGuid(), overlapStart, overlapEnd, "tok-overlap", 500m, "p2"));
            res2.IsFailure.Should().BeTrue();
            res2.Error.Code.Should().Be(BookingErrors.SlotOverlap.Code);
        }

        // Adjacent slot: 17:00 - 18:00 (Starts exactly when slot 1 ends) -> ALLOWED (exclusive upper bound)
        var adjacentStart = slot1End;
        var adjacentEnd = adjacentStart.AddHours(1);
        var quoteAdjacent = new PriceQuoteSnapshot(_venueId, pitch, adjacentStart, adjacentEnd, 60, "EGP", 500m, 0m, null, 500m, 100m, "Fixed", [new PriceLineSnapshot(adjacentStart, adjacentEnd, 500m, 500m, [])], _nowUtc);

        await using (var db3 = _database.CreateContext(clock))
        {
            var handler3 = CreateHandler(db3, clock, quoteAdjacent);
            var res3 = await handler3.HandleAsync(new CreateBookingCommand(pitch, Guid.NewGuid(), adjacentStart, adjacentEnd, "tok-adj", 500m, "p3"));
            res3.IsSuccess.Should().BeTrue("adjacent slots sharing an endpoint must be permitted");
        }
    }

    // =========================================================================
    // 5. Penalty Cooldown for Expired Holds (ADR-0008 §1)
    // =========================================================================

    [Fact(Skip = "BUG-007: ADR-0008 6-hour temporary booking lock after 3 expired holds within 24 hours is not implemented")]
    public async Task PenaltyCooldown_ThreeExpiredHoldsIn24Hours_Places6HourBookingLockAsync()
    {
        var clock = new FakeClock(_nowUtc);
        var playerId = Guid.NewGuid();

        // Simulate 3 holds that expired without deposit proof within the last 24 hours
        for (var i = 0; i < 3; i++)
        {
            var pitch = Guid.NewGuid();
            var start = _nowUtc.AddHours(2 + i);
            var end = start.AddHours(1);
            var quote = new PriceQuoteSnapshot(_venueId, pitch, start, end, 60, "EGP", 500m, 0m, null, 500m, 100m, "Fixed", [new PriceLineSnapshot(start, end, 500m, 500m, [])], _nowUtc);

            await using var db = _database.CreateContext(clock);
            var handler = CreateHandler(db, clock, quote);
            await handler.HandleAsync(new CreateBookingCommand(pitch, playerId, start, end, $"tok-{i}", 500m, playerId.ToString()));

            // Advance past hold window to expire
            clock.Advance(TimeSpan.FromMinutes(20));
        }

        // 4th hold attempt immediately after 3 expired holds:
        // ADR-0008 requires: "If a player triggers 3 expired holds within 24 hours without submitting payment proof, a 6-hour temporary booking lock is placed on the player account."
        var testPitch = Guid.NewGuid();
        var testStart = clock.UtcNow.AddHours(2);
        var testEnd = testStart.AddHours(1);
        var testQuote = new PriceQuoteSnapshot(_venueId, testPitch, testStart, testEnd, 60, "EGP", 500m, 0m, null, 500m, 100m, "Fixed", [new PriceLineSnapshot(testStart, testEnd, 500m, 500m, [])], clock.UtcNow);

        await using var finalDb = _database.CreateContext(clock);
        var finalHandler = CreateHandler(finalDb, clock, testQuote);
        var lockedResult = await finalHandler.HandleAsync(new CreateBookingCommand(testPitch, playerId, testStart, testEnd, "tok-lock", 500m, playerId.ToString()));

        // Should be rejected due to 6-hour penalty lock
        lockedResult.IsFailure.Should().BeTrue("player with 3 expired holds in 24h must be locked out for 6 hours");
        lockedResult.Error.Code.Should().Be("Booking.AccountLockedForHoarding");
    }

    // =========================================================================
    // Stubs
    // =========================================================================

    private sealed class StubQuoteValidator(PriceQuoteSnapshot quote) : IBookingQuoteValidator
    {
        public Task<Result<PriceQuoteSnapshot>> ValidateAsync(
            string quoteToken, Guid playerId, Guid pitchId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(quote));
    }

    private sealed class StubPolicyProvider(BookingPolicySnapshot? policy) : IBookingPolicyProvider
    {
        public Task<BookingPolicySnapshot?> GetCurrentSnapshotAsync(Guid venueId, CancellationToken cancellationToken = default) =>
            Task.FromResult(policy);
    }

    private sealed class NoOpAvailabilityInvalidator : IAvailabilityInvalidator
    {
        public Task InvalidatePitchAsync(Guid pitchId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateVenueAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpHoldExpiryScheduler : IHoldExpiryScheduler
    {
        public void ScheduleHoldExpiry(Guid bookingId, DateTime holdExpiresAtUtc) { }
    }

    private sealed class NoOpRealtimeBroadcaster : IBookingRealtimeBroadcaster
    {
        public Task BroadcastSlotStatusChangedAsync(Guid venueId, Guid pitchId, DateTime startUtc, DateTime endUtc, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
