using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure.Builders;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure.Jobs;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// Releasing lapsed holds (TASK-036) against a real PostgreSQL. A hold in these tests lasts 15 minutes from the moment the
/// booking is built, so the job runs either "now" (nothing has lapsed) or 20 minutes later (everything pending has).
/// </summary>
public sealed class ExpireBookingHoldsJobTests : IClassFixture<BookingDatabaseFixture>
{
    private static readonly DateTime _start = new(DateTime.UtcNow.Year + 7, 10, 1, 18, 0, 0, DateTimeKind.Utc);

    private readonly BookingDatabaseFixture _database;

    public ExpireBookingHoldsJobTests(BookingDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task TheSweep_ExpiresLapsedPendingHolds_AndLeavesEverythingElseAloneAsync()
    {
        var pitchId = Guid.NewGuid();
        var lapsed = await SaveAsync(pitchId);
        var withProof = await SaveAsync(withProof: true);
        var cancelled = await SaveAsync(status: BookingStatus.Cancelled);

        await RunSweepAsync(later: true);

        (await StatusAsync(lapsed)).Should().Be(BookingStatus.Expired);
        (await StatusAsync(withProof)).Should().Be(BookingStatus.ProofSubmitted, "a proof was sent, so the hold no longer applies");
        (await StatusAsync(cancelled)).Should().Be(BookingStatus.Cancelled);
        await using var verify = _database.CreateContext();
        (await verify.Bookings.Include(b => b.History).SingleAsync(b => b.Id == lapsed)).History.Should().Contain(h => h.ToStatus == BookingStatus.Expired);

        var rebook = async () =>
        {
            await using var context = _database.CreateContext();
            context.Bookings.Add(new BookingBuilder().ForPitch(pitchId).WithTimeSlot(_start, _start.AddHours(1)).Build());
            await context.SaveChangesAsync();
        };
        await rebook.Should().NotThrowAsync("an expired hold frees its slot");
    }

    [Fact]
    public async Task TheSweep_DoesNothingWhileHoldsAreStillRunningAsync()
    {
        var running = await SaveAsync();

        await RunSweepAsync(later: false);

        (await StatusAsync(running)).Should().Be(BookingStatus.PendingDeposit);
    }

    [Fact]
    public async Task ThePlayerIsToldOnlyAfterTheExpiryIsSaved_WithTheRealHoldLengthAsync()
    {
        var id = await SaveAsync();
        var publisher = new StatusAtPublish(_database, id);

        await RunSweepAsync(later: true, publisher);

        publisher.Seen.Should().ContainSingle().Which.Status.Should().Be(BookingStatus.Expired, "nothing may be announced before the expiry is stored");
        publisher.Seen.Single().Request.Data["HoldDurationMinutes"].Should().Be("15");
        publisher.Seen.Single().Request.UserId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task OneFailingSideEffect_DoesNotStopTheRestOfTheBatchAsync()
    {
        var ids = new[] { await SaveAsync(), await SaveAsync(), await SaveAsync() };

        await RunSweepAsync(later: true, new ThrowingPublisher());

        foreach (var id in ids)
        {
            (await StatusAsync(id)).Should().Be(BookingStatus.Expired);
        }
    }

    [Fact]
    public async Task TheScheduledJob_ExpiresALapsedHold_AndIgnoresEverythingElseAsync()
    {
        var lapsed = await SaveAsync();
        var running = await SaveAsync();
        var withProof = await SaveAsync(withProof: true);

        await Job(later: true).ExecuteForBookingAsync(lapsed);
        await Job(later: false).ExecuteForBookingAsync(running);
        await Job(later: true).ExecuteForBookingAsync(withProof);
        var unknown = async () => await Job(later: true).ExecuteForBookingAsync(Guid.NewGuid());

        (await StatusAsync(lapsed)).Should().Be(BookingStatus.Expired);
        (await StatusAsync(running)).Should().Be(BookingStatus.PendingDeposit);
        (await StatusAsync(withProof)).Should().Be(BookingStatus.ProofSubmitted);
        await unknown.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunningTheSweepTwice_ExpiresEachBookingOnceAsync()
    {
        var id = await SaveAsync();
        var publisher = new StatusAtPublish(_database, id);

        await RunSweepAsync(later: true, publisher);
        await RunSweepAsync(later: true, publisher);

        publisher.Seen.Should().ContainSingle("a repeated run must not announce the same expiry again");
        await using var verify = _database.CreateContext();
        (await verify.Bookings.Include(b => b.History).SingleAsync(b => b.Id == id)).History.Count(h => h.ToStatus == BookingStatus.Expired).Should().Be(1);
    }

    private async Task<Guid> SaveAsync(Guid? pitchId = null, bool withProof = false, BookingStatus status = BookingStatus.PendingDeposit)
    {
        var playerId = Guid.NewGuid();
        var booking = new BookingBuilder().ForPlayer(playerId).ForPitch(pitchId ?? Guid.NewGuid()).WithTimeSlot(_start, _start.AddHours(1)).Build();
        if (withProof)
        {
            var proofId = Guid.NewGuid();
            booking.SubmitProof(proofId, playerId.ToString("D"), DateTime.UtcNow).IsSuccess.Should().BeTrue();
            booking.AddProof(PaymentProof.Create(proofId, booking.Id, Guid.NewGuid(), "media/key", "proof.webp", "image/webp", 1234, Convert.ToHexString(Guid.NewGuid().ToByteArray()), playerId.ToString("D"), DateTime.UtcNow));
        }

        if (status == BookingStatus.Cancelled)
        {
            booking.Cancel("Test", playerId.ToString("D"), isStaff: false, DateTime.UtcNow).IsSuccess.Should().BeTrue();
        }

        await using var context = _database.CreateContext();
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
        return booking.Id;
    }

    private async Task<BookingStatus> StatusAsync(Guid id)
    {
        await using var context = _database.CreateContext();
        return (await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == id)).Status;
    }

    private Task RunSweepAsync(bool later, INotificationPublisher? publisher = null) => Job(later, publisher).ExecuteRecurringSweepAsync();

    private ExpireBookingHoldsJob Job(bool later, INotificationPublisher? publisher = null) => new(
        _database.CreateContext(),
        new FakeClock(DateTime.UtcNow.AddMinutes(later ? 20 : 0)),
        new QuietInvalidator(),
        new QuietBroadcaster(),
        NullLogger<ExpireBookingHoldsJob>.Instance,
        publisher);

    private sealed class QuietInvalidator : IAvailabilityInvalidator
    {
        public Task InvalidatePitchAsync(Guid pitchId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task InvalidateVenueAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class QuietBroadcaster : IBookingRealtimeBroadcaster
    {
        public Task BroadcastSlotStatusChangedAsync(Guid venueId, Guid pitchId, DateTime startUtc, DateTime endUtc, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingPublisher : INotificationPublisher
    {
        public Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("The notification service is down");
    }

    /// <summary>On publish for its booking, reads the booking from the database to see what had been saved by then.</summary>
    private sealed class StatusAtPublish(BookingDatabaseFixture database, Guid bookingId) : INotificationPublisher
    {
        private readonly List<(BookingStatus Status, NotificationRequest Request)> _seen = [];

        public IReadOnlyList<(BookingStatus Status, NotificationRequest Request)> Seen => _seen;

        public async Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            if (request.DedupeKey != $"booking-expired:{bookingId:D}")
            {
                return;
            }

            await using var context = database.CreateContext();
            var status = (await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId, cancellationToken)).Status;
            _seen.Add((status, request));
        }
    }
}
