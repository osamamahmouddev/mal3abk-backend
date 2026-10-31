using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure.Builders;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Bookings.SubmitProof;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Events;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// Submitting a payment proof (TASK-034, TASK-408) against a real PostgreSQL: the receipt must carry a content hash, the
/// same receipt cannot be active on two bookings (even when two requests race), and a side effect that fails after the
/// proof is saved must not turn into an error.
/// </summary>
public sealed class SubmitProofTests : IClassFixture<BookingDatabaseFixture>
{
    private static readonly DateTime _start = new(DateTime.UtcNow.Year + 4, 7, 1, 18, 0, 0, DateTimeKind.Utc);

    private readonly BookingDatabaseFixture _database;

    public SubmitProofTests(BookingDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task AValidProof_IsStoredWithItsHashAndMovesTheBookingOnAsync()
    {
        var (bookingId, playerId) = await SaveBookingAsync();
        var hash = NewHash();
        var mediaId = Guid.NewGuid();

        var result = await SubmitAsync(bookingId, playerId, hash, mediaId: mediaId);

        result.IsSuccess.Should().BeTrue();
        await using var verify = _database.CreateContext();
        var booking = await verify.Bookings.Include(b => b.Proofs).SingleAsync(b => b.Id == bookingId);
        booking.Status.Should().Be(BookingStatus.ProofSubmitted);
        var proof = booking.Proofs.Should().ContainSingle().Subject;
        proof.Sha256Hash.Should().Be(hash);
        proof.MediaId.Should().Be(mediaId, "the download link is signed from the media id, so it must be stored");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AProofWithoutAContentHash_IsRefusedNotWavedThroughAsync(string hash)
    {
        var (bookingId, playerId) = await SaveBookingAsync();

        var result = await SubmitAsync(bookingId, playerId, hash);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Proof.HashRequired");
        await using var verify = _database.CreateContext();
        (await verify.PaymentProofs.CountAsync(p => p.BookingId == bookingId)).Should().Be(0);
    }

    [Fact]
    public async Task TheSameReceiptOnAnotherBooking_IsRefusedAsync()
    {
        var hash = NewHash();
        var (firstId, firstPlayer) = await SaveBookingAsync();
        var (secondId, secondPlayer) = await SaveBookingAsync();
        (await SubmitAsync(firstId, firstPlayer, hash)).IsSuccess.Should().BeTrue();

        var reuse = await SubmitAsync(secondId, secondPlayer, hash);

        reuse.IsFailure.Should().BeTrue();
        reuse.Error.Code.Should().Be("Proof.DuplicateReceipt");
        await using var verify = _database.CreateContext();
        (await verify.Bookings.SingleAsync(b => b.Id == secondId)).Status.Should().Be(BookingStatus.PendingDeposit);
    }

    [Fact]
    public async Task AReceiptThatWasRejected_CanBeUsedAgainAsync()
    {
        var hash = NewHash();
        var (firstId, firstPlayer) = await SaveBookingAsync();
        var (secondId, secondPlayer) = await SaveBookingAsync();
        (await SubmitAsync(firstId, firstPlayer, hash)).IsSuccess.Should().BeTrue();
        await using (var review = _database.CreateContext())
        {
            var proof = await review.PaymentProofs.SingleAsync(p => p.BookingId == firstId);
            proof.Review(isApproved: false, "Unreadable", "staff", DateTime.UtcNow);
            await review.SaveChangesAsync();
        }

        var reuse = await SubmitAsync(secondId, secondPlayer, hash);

        reuse.IsSuccess.Should().BeTrue("a rejected receipt no longer counts as the active one");
    }

    [Fact]
    public async Task TwoRequestsRacingWithTheSameReceipt_ExactlyOneWinsAsync()
    {
        var hash = NewHash();
        var contenders = new List<(Guid BookingId, Guid PlayerId)>();
        for (var i = 0; i < 6; i++)
        {
            contenders.Add(await SaveBookingAsync());
        }

        var results = await Task.WhenAll(contenders.Select(c => Task.Run(() => SubmitAsync(c.BookingId, c.PlayerId, hash))));

        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Where(r => r.IsFailure).Should().OnlyContain(r => r.Error.Code == "Proof.DuplicateReceipt");
        await using var verify = _database.CreateContext();
        (await verify.PaymentProofs.CountAsync(p => p.Sha256Hash == hash)).Should().Be(1);
    }

    [Fact]
    public async Task TwoSubmissionsForOneBooking_NeverCrashAsync()
    {
        var (bookingId, playerId) = await SaveBookingAsync();

        var results = await Task.WhenAll(
            Task.Run(() => SubmitAsync(bookingId, playerId, NewHash())),
            Task.Run(() => SubmitAsync(bookingId, playerId, NewHash())));

        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Single(r => r.IsFailure).Error.Type.Should().Be(ErrorType.Conflict);
        await using var verify = _database.CreateContext();
        (await verify.PaymentProofs.CountAsync(p => p.BookingId == bookingId)).Should().Be(1);
    }

    [Fact]
    public async Task AnotherPlayersBooking_CannotReceiveAProofAsync()
    {
        var (bookingId, _) = await SaveBookingAsync();

        var result = await SubmitAsync(bookingId, Guid.NewGuid(), NewHash());

        result.IsFailure.Should().BeTrue();
        await using var verify = _database.CreateContext();
        (await verify.PaymentProofs.CountAsync(p => p.BookingId == bookingId)).Should().Be(0);
    }

    [Fact]
    public async Task WhenALiveUpdateFailsAfterTheSave_TheProofIsStillAcceptedAsync()
    {
        var (bookingId, playerId) = await SaveBookingAsync();

        var result = await SubmitAsync(bookingId, playerId, NewHash(), broadcaster: new ThrowingBroadcaster());

        result.IsSuccess.Should().BeTrue();
        await using var verify = _database.CreateContext();
        (await verify.Bookings.SingleAsync(b => b.Id == bookingId)).Status.Should().Be(BookingStatus.ProofSubmitted);
    }

    [Fact]
    public async Task TheDatabase_RefusesTheSameActiveReceiptOnTwoBookings_WhateverTheApplicationDoesAsync()
    {
        var hash = NewHash();
        var (firstId, _) = await SaveBookingAsync();
        var (secondId, _) = await SaveBookingAsync();
        await AddProofAsync(firstId, hash);

        var second = async () => await AddProofAsync(secondId, hash);

        var failure = await second.Should().ThrowAsync<DbUpdateException>();
        BookingPersistenceErrors.IsDuplicateProofHash(failure.Which).Should().BeTrue();
    }

    [Fact]
    public async Task TheDatabase_LetsARejectedReceiptBeReusedAndIgnoresProofsWithoutAHashAsync()
    {
        var hash = NewHash();
        var (firstId, _) = await SaveBookingAsync();
        var (secondId, _) = await SaveBookingAsync();
        var (thirdId, _) = await SaveBookingAsync();
        await AddProofAsync(firstId, hash, rejected: true);

        var reuse = async () => await AddProofAsync(secondId, hash);
        var legacyA = async () => await AddProofAsync(secondId, string.Empty);
        var legacyB = async () => await AddProofAsync(thirdId, string.Empty);

        await reuse.Should().NotThrowAsync("a rejected receipt no longer counts");
        await legacyA.Should().NotThrowAsync();
        await legacyB.Should().NotThrowAsync("proofs from before hashing have no hash and must not collide");
    }

    private async Task AddProofAsync(Guid bookingId, string hash, bool rejected = false)
    {
        var proof = PaymentProof.Create(Guid.NewGuid(), bookingId, Guid.NewGuid(), "media/key/large.webp", "proof.webp", "image/webp", 1234, hash, "player", DateTime.UtcNow);
        if (rejected)
        {
            proof.Review(isApproved: false, "Unreadable", "staff", DateTime.UtcNow);
        }

        await using var context = _database.CreateContext();
        context.PaymentProofs.Add(proof);
        await context.SaveChangesAsync();
    }

    private static string NewHash() => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));

    private async Task<(Guid BookingId, Guid PlayerId)> SaveBookingAsync()
    {
        var playerId = Guid.NewGuid();
        var booking = new BookingBuilder().ForPlayer(playerId).WithTimeSlot(_start, _start.AddHours(1)).Build();
        await using var context = _database.CreateContext();
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
        return (booking.Id, playerId);
    }

    private async Task<Result> SubmitAsync(Guid bookingId, Guid playerId, string hash, IBookingRealtimeBroadcaster? broadcaster = null, Guid? mediaId = null)
    {
        await using var context = _database.CreateContext();
        var handler = new SubmitProofHandler(
            context, new FakeClock(DateTime.UtcNow), broadcaster ?? new QuietBroadcaster(), new QuietDispatcher(), NullLogger<SubmitProofHandler>.Instance);
        return await handler.HandleAsync(new SubmitProofCommand(
            bookingId, playerId, mediaId ?? Guid.NewGuid(), "media/key/large.webp", "proof.webp", "image/webp", 1234, hash));
    }

    private sealed class QuietDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class QuietBroadcaster : IBookingRealtimeBroadcaster
    {
        public virtual Task BroadcastSlotStatusChangedAsync(Guid venueId, Guid pitchId, DateTime startUtc, DateTime endUtc, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public virtual Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingBroadcaster : QuietBroadcaster
    {
        public override Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SignalR is down");
    }
}
