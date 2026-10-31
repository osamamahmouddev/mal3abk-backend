using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure.Builders;
using Malabak.Modules.Booking.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// The state machine only matters if its transitions survive a round trip through the database. Each step below runs in
/// a fresh context, as a real request would: load the stored booking, change its state, save.
/// </summary>
public sealed class BookingPersistenceTests : IClassFixture<BookingDatabaseFixture>
{
    private readonly BookingDatabaseFixture _database;

    public BookingPersistenceTests(BookingDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task EveryTransitionOnAStoredBooking_IsSavedWithItsHistoryRowAsync()
    {
        var now = DateTime.UtcNow;
        var booking = new BookingBuilder().WithTimeSlot(now.AddDays(30), now.AddDays(30).AddHours(1)).Build();
        await using (var create = _database.CreateContext())
        {
            create.Bookings.Add(booking);
            await create.SaveChangesAsync();
        }

        await ChangeAsync(booking.Id, b => b.SubmitProof("player", now));
        await ChangeAsync(booking.Id, b => b.MarkUnderReview("staff", now));
        await ChangeAsync(booking.Id, b => b.Confirm("staff", now));
        await ChangeAsync(booking.Id, b => b.Cancel("Player cancelled", "player", now));

        await using var verify = _database.CreateContext();
        var stored = await verify.Bookings.Include(b => b.History).SingleAsync(b => b.Id == booking.Id);

        stored.Status.Should().Be(BookingStatus.Cancelled);
        stored.History.OrderBy(h => h.ChangedAtUtc).ThenBy(h => h.ToStatus).Select(h => (h.FromStatus, h.ToStatus)).Should().BeEquivalentTo(
        [
            (BookingStatus.Draft, BookingStatus.PendingDeposit),
            (BookingStatus.PendingDeposit, BookingStatus.ProofSubmitted),
            (BookingStatus.ProofSubmitted, BookingStatus.UnderReview),
            (BookingStatus.UnderReview, BookingStatus.Confirmed),
            (BookingStatus.Confirmed, BookingStatus.Cancelled),
        ]);
        stored.History.Single(h => h.ToStatus == BookingStatus.Cancelled).Reason.Should().Be("Player cancelled");
        stored.History.Single(h => h.ToStatus == BookingStatus.Confirmed).ChangedByUserId.Should().Be("staff");
    }

    private async Task ChangeAsync(Guid bookingId, Func<Modules.Booking.Domain.Booking, Malabak.SharedKernel.Result> change)
    {
        await using var context = _database.CreateContext();
        var booking = await context.Bookings.Include(b => b.History).SingleAsync(b => b.Id == bookingId);
        change(booking).IsSuccess.Should().BeTrue();
        await context.SaveChangesAsync();
    }
}
