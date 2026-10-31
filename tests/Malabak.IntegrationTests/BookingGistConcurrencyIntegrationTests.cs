using System.Collections.Concurrent;
using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Bookings.CreateBooking;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Events;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.IntegrationTests;

public sealed class BookingGistConcurrencyIntegrationTests
{
    private readonly string _connectionString = IntegrationTestEnvironment.PostgresConnectionString;

    [Fact]
    public async Task ConcurrentBookingCreation_With20Threads_EnsuresExactlyOneSucceedsAndNineteenFailAsync()
    {
        // Arrange
        const int concurrentThreads = 20;
        var pitchId = Guid.NewGuid();
        var venueId = Guid.NewGuid();
        var nowUtc = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var startUtc = nowUtc.AddHours(2);
        var endUtc = nowUtc.AddHours(3);

        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(_connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", BookingDbContext.Schema))
            .Options;

        // Ensure database schema and migrations are applied
        await using (var seedContext = new BookingDbContext(options))
        {
            await seedContext.Database.MigrateAsync();
        }

        var samplePrice = new PriceQuoteSnapshot(
            venueId,
            pitchId,
            startUtc,
            endUtc,
            60,
            "EGP",
            500m,
            0m,
            null,
            500m,
            100m,
            "Fixed",
            [new PriceLineSnapshot(startUtc, endUtc, 500m, 500m, [])],
            nowUtc);

        var samplePolicy = new BookingPolicySnapshot(
            Guid.NewGuid(),
            venueId,
            1,
            "Fixed",
            100m,
            15,
            120,
            24,
            "FullRefund",
            100m,
            "ForfeitDeposit",
            15,
            true,
            24,
            true,
            "Standard policy",
            nowUtc);

        var clock = new FakeClock(nowUtc);
        var quoteValidator = new StubQuoteValidator(samplePrice);
        var policyProvider = new StubPolicyProvider(samplePolicy);
        var refGenerator = new BookingReferenceGenerator();
        var availabilityInvalidator = new NoOpAvailabilityInvalidator();
        var domainEventDispatcher = new NoOpDomainEventDispatcher();
        var holdExpiryScheduler = new NoOpHoldExpiryScheduler();
        var realtimeBroadcaster = new NoOpRealtimeBroadcaster();

        var results = new ConcurrentBag<Result<CreateBookingResponse>>();

        // Act - 20 concurrent threads trying to book the exact same pitch and time slot simultaneously
        using var countdown = new CountdownEvent(concurrentThreads);
        var tasks = Enumerable.Range(0, concurrentThreads).Select(async i =>
        {
            await Task.Yield();
            // Each thread uses its own DbContext instance
            await using var dbContext = new BookingDbContext(options);
            var handler = new CreateBookingHandler(
                dbContext,
                clock,
                quoteValidator,
                policyProvider,
                refGenerator,
                availabilityInvalidator,
                domainEventDispatcher,
                holdExpiryScheduler,
                realtimeBroadcaster,
                NullLogger<CreateBookingHandler>.Instance);

            var playerId = Guid.NewGuid(); // Unique player for each thread
            var command = new CreateBookingCommand(
                pitchId,
                playerId,
                startUtc,
                endUtc,
                QuoteToken: "valid-quote-token",
                ExpectedPrice: 500m,
                ActorUserId: playerId.ToString());

            // Signal readiness and wait for all threads to be ready
            countdown.Signal();
            countdown.Wait();

            var result = await handler.HandleAsync(command);
            results.Add(result);
        });

        await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(concurrentThreads);

        var successful = results.Where(r => r.IsSuccess).ToList();
        var failed = results.Where(r => r.IsFailure).ToList();

        successful.Should().ContainSingle("exactly one concurrent booking hold must succeed");
        failed.Should().HaveCount(concurrentThreads - 1, "all other 19 concurrent attempts must fail with slot conflict");

        foreach (var failure in failed)
        {
            failure.Error.Code.Should().Be(BookingErrors.SlotOverlap.Code);
        }

        // Verify database state: exactly 1 booking exists for this pitch and time slot
        await using (var verifyContext = new BookingDbContext(options))
        {
            var activeBookings = await verifyContext.Bookings.AsNoTracking()
                .Where(b => b.PitchId == pitchId && b.StartUtc < endUtc && b.EndUtc > startUtc && !b.IsDeleted)
                .ToListAsync();

            activeBookings.Should().ContainSingle("zero double bookings occur under race condition");
            activeBookings[0].Id.Should().Be(successful[0].Value.BookingId);
        }
    }

    private sealed class StubQuoteValidator(PriceQuoteSnapshot quote) : IBookingQuoteValidator
    {
        public Task<Result<PriceQuoteSnapshot>> ValidateAsync(
            string quoteToken, Guid playerId, Guid pitchId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(quote));
        }
    }

    private sealed class StubPolicyProvider(BookingPolicySnapshot policy) : IBookingPolicyProvider
    {
        public Task<BookingPolicySnapshot?> GetCurrentSnapshotAsync(Guid venueId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<BookingPolicySnapshot?>(policy);
        }
    }

    private sealed class NoOpAvailabilityInvalidator : IAvailabilityInvalidator
    {
        public Task InvalidatePitchAsync(Guid pitchId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateVenueAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
