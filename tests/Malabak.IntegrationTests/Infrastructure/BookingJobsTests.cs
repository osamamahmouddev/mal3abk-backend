using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure.Builders;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure.Jobs;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// The proof retention job (TASK-408, ADR-0007) and the completion job (TASK-405) against a real PostgreSQL.
/// </summary>
public sealed class BookingJobsTests : IClassFixture<BookingDatabaseFixture>
{
    private static readonly DateTime _slot = new(DateTime.UtcNow.Year + 8, 11, 1, 18, 0, 0, DateTimeKind.Utc);

    private readonly BookingDatabaseFixture _database;

    public BookingJobsTests(BookingDatabaseFixture database)
    {
        _database = database;
    }

    // ----- proof retention -----

    [Fact]
    public async Task ARejectedReceipt_IsDeletedAfter90Days_NotBeforeAsync()
    {
        var old = await SaveProofAsync(ProofKind.Rejected, ageDays: 100);
        var recent = await SaveProofAsync(ProofKind.Rejected, ageDays: 30);
        var files = new RecordingFiles();

        await RetentionJob(files).ExecuteRecurringCleanupAsync();

        files.Deleted.Should().Contain(old.MediaId).And.NotContain(recent.MediaId);
        (await ArchivedAsync(old.ProofId)).Should().BeTrue();
        (await ArchivedAsync(recent.ProofId)).Should().BeFalse();
    }

    [Fact]
    public async Task AnApprovedReceipt_IsKeptFor2Years_ThenDeletedAsync()
    {
        var hundredDays = await SaveProofAsync(ProofKind.Approved, ageDays: 100);
        var threeYears = await SaveProofAsync(ProofKind.Approved, ageDays: 1100);
        var files = new RecordingFiles();

        await RetentionJob(files).ExecuteRecurringCleanupAsync();

        files.Deleted.Should().Contain(threeYears.MediaId).And.NotContain(hundredDays.MediaId, "a confirmed booking's receipt is kept for 2 years");
    }

    [Fact]
    public async Task AReceiptOnACancelledBooking_FollowsTheShortRetentionAsync()
    {
        var cancelled = await SaveProofAsync(ProofKind.OnCancelledBooking, ageDays: 100);
        var stillOpen = await SaveProofAsync(ProofKind.Undecided, ageDays: 100);
        var files = new RecordingFiles();

        await RetentionJob(files).ExecuteRecurringCleanupAsync();

        files.Deleted.Should().Contain(cancelled.MediaId).And.NotContain(stillOpen.MediaId, "a proof still awaiting review is never deleted");
    }

    [Fact]
    public async Task AnImageThatCannotBeDeleted_StaysDueAndIsRetried_WithoutStoppingTheRestAsync()
    {
        var first = await SaveProofAsync(ProofKind.Rejected, ageDays: 200);
        var second = await SaveProofAsync(ProofKind.Rejected, ageDays: 190);
        var files = new RecordingFiles { FailFor = first.MediaId };

        await RetentionJob(files).ExecuteRecurringCleanupAsync();

        (await ArchivedAsync(first.ProofId)).Should().BeFalse("its image is still there, so it stays due");
        (await ArchivedAsync(second.ProofId)).Should().BeTrue();

        files.FailFor = null;
        await RetentionJob(files).ExecuteRecurringCleanupAsync();
        (await ArchivedAsync(first.ProofId)).Should().BeTrue("tomorrow's run finishes the job");
    }

    [Fact]
    public async Task ARunThatFindsNothingNewDeletesNothingAgainAsync()
    {
        var proof = await SaveProofAsync(ProofKind.Rejected, ageDays: 120);
        var files = new RecordingFiles();

        await RetentionJob(files).ExecuteRecurringCleanupAsync();
        files.Deleted.Clear();
        await RetentionJob(files).ExecuteRecurringCleanupAsync();

        files.Deleted.Should().NotContain(proof.MediaId);
    }

    // ----- completion -----

    [Fact]
    public async Task ConfirmedBookingsThatHaveEnded_AreCompleted_OthersAreLeftAloneAsync()
    {
        var ended = await SaveConfirmedAsync(endedHoursAgo: 2);
        var running = await SaveConfirmedAsync(endedHoursAgo: -2);
        var pending = await SaveBookingAsync(BookingStatus.PendingDeposit);

        await CompleteJob(new QuietBroadcaster()).ExecuteRecurringSweepAsync();

        (await StatusAsync(ended)).Should().Be(BookingStatus.Completed);
        (await StatusAsync(running)).Should().Be(BookingStatus.Confirmed, "the match has not finished");
        (await StatusAsync(pending)).Should().Be(BookingStatus.PendingDeposit);
        await using var verify = _database.CreateContext();
        (await verify.Bookings.Include(b => b.History).SingleAsync(b => b.Id == ended)).History.Should().Contain(h => h.ToStatus == BookingStatus.Completed);
    }

    [Fact]
    public async Task TheCompletionIsAnnounced_OnlyAfterItIsSaved_AndABrokenAnnouncementDoesNotStopTheBatchAsync()
    {
        var first = await SaveConfirmedAsync(endedHoursAgo: 3);
        var second = await SaveConfirmedAsync(endedHoursAgo: 3);
        var broadcaster = new StatusAtBroadcast(_database, [first, second], throwOnEvery: true);

        await CompleteJob(broadcaster).ExecuteRecurringSweepAsync();

        (await StatusAsync(first)).Should().Be(BookingStatus.Completed);
        (await StatusAsync(second)).Should().Be(BookingStatus.Completed, "one failed live update must not leave the next booking uncompleted");
        broadcaster.Seen.Should().OnlyContain(s => s == BookingStatus.Completed, "nothing may be announced before the completion is stored");
    }

    // ----- helpers -----

    private enum ProofKind
    {
        Rejected,
        Approved,
        Undecided,
        OnCancelledBooking,
    }

    private ProofRetentionJob RetentionJob(IProofFileStore files) =>
        new(_database.CreateContext(), files, new FakeClock(DateTime.UtcNow), NullLogger<ProofRetentionJob>.Instance);

    private CompleteBookingsJob CompleteJob(IBookingRealtimeBroadcaster broadcaster) =>
        new(_database.CreateContext(), new FakeClock(DateTime.UtcNow), broadcaster, NullLogger<CompleteBookingsJob>.Instance);

    private async Task<(Guid ProofId, Guid MediaId)> SaveProofAsync(ProofKind kind, int ageDays)
    {
        var playerId = Guid.NewGuid();
        var booking = new BookingBuilder().ForPlayer(playerId).WithTimeSlot(_slot, _slot.AddHours(1)).Build();
        var proofId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var uploadedAt = DateTime.UtcNow.AddDays(-ageDays);
        var actor = playerId.ToString("D");

        booking.SubmitProof(proofId, actor, uploadedAt).IsSuccess.Should().BeTrue();
        var proof = PaymentProof.Create(proofId, booking.Id, mediaId, "media/key", "proof.webp", "image/webp", 1234, Convert.ToHexString(Guid.NewGuid().ToByteArray()), actor, uploadedAt);
        booking.AddProof(proof);
        switch (kind)
        {
            case ProofKind.Rejected:
                proof.Review(isApproved: false, "Unreadable", "staff", uploadedAt);
                break;
            case ProofKind.Approved:
                proof.Review(isApproved: true, null, "staff", uploadedAt);
                booking.Confirm("staff", uploadedAt).IsSuccess.Should().BeTrue();
                break;
            case ProofKind.OnCancelledBooking:
                booking.Cancel("Changed my mind", actor, isStaff: false, uploadedAt).IsSuccess.Should().BeTrue();
                break;
            case ProofKind.Undecided:
                break;
        }

        await using var context = _database.CreateContext();
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
        return (proofId, mediaId);
    }

    private async Task<Guid> SaveConfirmedAsync(int endedHoursAgo)
    {
        var playerId = Guid.NewGuid();
        var end = DateTime.UtcNow.AddHours(-endedHoursAgo);
        var booking = new BookingBuilder().ForPlayer(playerId).WithTimeSlot(end.AddHours(-1), end).Build();
        var proofId = Guid.NewGuid();
        booking.SubmitProof(proofId, playerId.ToString("D"), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        booking.Confirm("staff", DateTime.UtcNow).IsSuccess.Should().BeTrue();

        await using var context = _database.CreateContext();
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
        return booking.Id;
    }

    private async Task<Guid> SaveBookingAsync(BookingStatus status)
    {
        status.Should().Be(BookingStatus.PendingDeposit);
        var booking = new BookingBuilder().WithTimeSlot(_slot, _slot.AddHours(1)).Build();
        await using var context = _database.CreateContext();
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
        return booking.Id;
    }

    private async Task<bool> ArchivedAsync(Guid proofId)
    {
        await using var context = _database.CreateContext();
        return (await context.PaymentProofs.AsNoTracking().SingleAsync(p => p.Id == proofId)).IsArchived;
    }

    private async Task<BookingStatus> StatusAsync(Guid id)
    {
        await using var context = _database.CreateContext();
        return (await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == id)).Status;
    }

    private sealed class RecordingFiles : IProofFileStore
    {
        public List<Guid> Deleted { get; } = [];

        public Guid? FailFor { get; set; }

        public Task DeleteAsync(Guid mediaId, CancellationToken cancellationToken = default)
        {
            if (mediaId == FailFor)
            {
                throw new InvalidOperationException("The object store is unavailable");
            }

            Deleted.Add(mediaId);
            return Task.CompletedTask;
        }
    }

    private class QuietBroadcaster : IBookingRealtimeBroadcaster
    {
        public virtual Task BroadcastSlotStatusChangedAsync(Guid venueId, Guid pitchId, DateTime startUtc, DateTime endUtc, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public virtual Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>On each update, reads that booking from the database to see what had been saved by then, then fails if asked to.</summary>
    private sealed class StatusAtBroadcast(BookingDatabaseFixture database, IReadOnlyCollection<Guid> bookingIds, bool throwOnEvery) : QuietBroadcaster
    {
        private readonly List<BookingStatus> _seen = [];

        public IReadOnlyList<BookingStatus> Seen => _seen;

        public override async Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc, CancellationToken cancellationToken = default)
        {
            if (bookingIds.Contains(bookingId))
            {
                await using var context = database.CreateContext();
                _seen.Add((await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId, cancellationToken)).Status);
            }

            if (throwOnEvery)
            {
                throw new InvalidOperationException("SignalR is down");
            }
        }
    }
}
