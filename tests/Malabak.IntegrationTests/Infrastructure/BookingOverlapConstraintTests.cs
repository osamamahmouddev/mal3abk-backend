using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure.Builders;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// Double-booking protection is enforced by the database (rule.md section 5): whatever the application does, and however
/// requests race, two active bookings of one pitch can never overlap.
/// </summary>
public sealed class BookingOverlapConstraintTests : IClassFixture<BookingDatabaseFixture>
{
    private const string _exclusionViolation = "23P01";

    private static readonly DateTime _baseStart = new(DateTime.UtcNow.Year + 1, 6, 1, 18, 0, 0, DateTimeKind.Utc);

    private readonly BookingDatabaseFixture _database;

    public BookingOverlapConstraintTests(BookingDatabaseFixture database)
    {
        _database = database;
    }

    public static TheoryData<BookingStatus> ActiveStatuses => new()
    {
        BookingStatus.PendingDeposit,
        BookingStatus.ProofSubmitted,
        BookingStatus.UnderReview,
        BookingStatus.Confirmed,
    };

    public static TheoryData<BookingStatus> ReleasedStatuses => new()
    {
        BookingStatus.Rejected,
        BookingStatus.Expired,
        BookingStatus.Cancelled,
    };

    [Theory]
    [MemberData(nameof(ActiveStatuses))]
    public async Task OverlappingBooking_WhileTheFirstIsActive_IsRejectedByTheDatabaseAsync(BookingStatus firstStatus)
    {
        var pitchId = Guid.NewGuid();
        await SaveAsync(Booking(pitchId, 0, 60, firstStatus));

        var overlapping = async () => await SaveAsync(Booking(pitchId, 30, 90, BookingStatus.PendingDeposit));

        var failure = await overlapping.Should().ThrowAsync<DbUpdateException>();
        failure.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(_exclusionViolation);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(10, 50)]
    [InlineData(-30, 30)]
    [InlineData(59, 120)]
    [InlineData(-60, 120)]
    public async Task EveryKindOfOverlap_IsRejectedAsync(int startOffsetMinutes, int endOffsetMinutes)
    {
        var pitchId = Guid.NewGuid();
        await SaveAsync(Booking(pitchId, 0, 60, BookingStatus.Confirmed));

        var overlapping = async () => await SaveAsync(Booking(pitchId, startOffsetMinutes, endOffsetMinutes, BookingStatus.PendingDeposit));

        var failure = await overlapping.Should().ThrowAsync<DbUpdateException>();
        failure.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(_exclusionViolation);
    }

    [Fact]
    public async Task BackToBackBookings_AreAllowedAsync()
    {
        var pitchId = Guid.NewGuid();
        await SaveAsync(Booking(pitchId, 0, 60, BookingStatus.Confirmed));

        var next = async () => await SaveAsync(Booking(pitchId, 60, 120, BookingStatus.PendingDeposit));
        var previous = async () => await SaveAsync(Booking(pitchId, -60, 0, BookingStatus.PendingDeposit));

        await next.Should().NotThrowAsync();
        await previous.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SameTimeOnAnotherPitch_IsAllowedAsync()
    {
        await SaveAsync(Booking(Guid.NewGuid(), 0, 60, BookingStatus.Confirmed));

        var otherPitch = async () => await SaveAsync(Booking(Guid.NewGuid(), 0, 60, BookingStatus.PendingDeposit));

        await otherPitch.Should().NotThrowAsync();
    }

    [Theory]
    [MemberData(nameof(ReleasedStatuses))]
    public async Task ReleasedBooking_NoLongerBlocksTheSlotAsync(BookingStatus releasedStatus)
    {
        var pitchId = Guid.NewGuid();
        await SaveAsync(Booking(pitchId, 0, 60, releasedStatus));

        var retry = async () => await SaveAsync(Booking(pitchId, 0, 60, BookingStatus.PendingDeposit));

        await retry.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CancellingTheActiveBooking_FreesTheSlotAsync()
    {
        var pitchId = Guid.NewGuid();
        await using (var context = _database.CreateContext())
        {
            var first = Booking(pitchId, 0, 60, BookingStatus.Confirmed);
            context.Bookings.Add(first);
            await context.SaveChangesAsync();

            first.Cancel("Player cancelled", "test", DateTime.UtcNow).IsSuccess.Should().BeTrue();
            await context.SaveChangesAsync();
        }

        var retry = async () => await SaveAsync(Booking(pitchId, 0, 60, BookingStatus.PendingDeposit));

        await retry.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ConcurrentRequestsForTheSameSlot_ExactlyOneWinsAsync()
    {
        const int contenders = 20;
        var pitchId = Guid.NewGuid();
        using var startGate = new ManualResetEventSlim(false);

        var attempts = Enumerable.Range(0, contenders).Select(_ => Task.Run(async () =>
        {
            await using var context = _database.CreateContext();
            context.Bookings.Add(Booking(pitchId, 0, 60, BookingStatus.PendingDeposit));
            startGate.Wait();
            try
            {
                await context.SaveChangesAsync();
                return (Won: true, SqlState: (string?)null);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg)
            {
                return (Won: false, SqlState: pg.SqlState);
            }
        })).ToList();

        startGate.Set();
        var outcomes = await Task.WhenAll(attempts);

        outcomes.Count(o => o.Won).Should().Be(1, "the database must let exactly one of the racing requests keep the slot");
        outcomes.Where(o => !o.Won).Should().OnlyContain(o => o.SqlState == _exclusionViolation, "every loser fails with the exclusion violation, not some other error");

        await using var verify = _database.CreateContext();
        (await verify.Bookings.CountAsync(b => b.PitchId == pitchId)).Should().Be(1);
    }

    private async Task SaveAsync(Modules.Booking.Domain.Booking booking)
    {
        await using var context = _database.CreateContext();
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
    }

    /// <summary>A booking of the pitch from <paramref name="startMinutes"/> to <paramref name="endMinutes"/> after the base time, moved to <paramref name="status"/>.</summary>
    private static Modules.Booking.Domain.Booking Booking(Guid pitchId, int startMinutes, int endMinutes, BookingStatus status)
    {
        var booking = new BookingBuilder()
            .ForPitch(pitchId)
            .WithBookingReference($"MLB-{Guid.NewGuid():N}"[..12].ToUpperInvariant())
            .WithTimeSlot(_baseStart.AddMinutes(startMinutes), _baseStart.AddMinutes(endMinutes))
            .Build();

        var now = DateTime.UtcNow;
        var moved = status switch
        {
            BookingStatus.PendingDeposit => Result(),
            BookingStatus.ProofSubmitted => booking.SubmitProof("test", now),
            BookingStatus.UnderReview => booking.SubmitProof("test", now).IsSuccess ? booking.MarkUnderReview("test", now) : Result(false),
            BookingStatus.Confirmed => booking.SubmitProof("test", now).IsSuccess ? booking.Confirm("test", now) : Result(false),
            BookingStatus.Rejected => booking.SubmitProof("test", now).IsSuccess ? booking.Reject("Unreadable receipt", "test", now) : Result(false),
            BookingStatus.Expired => booking.Expire(now),
            BookingStatus.Cancelled => booking.Cancel("Player cancelled", "test", now),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not needed by these tests."),
        };

        moved.IsSuccess.Should().BeTrue($"the test booking must be able to reach {status}");
        return booking;

        static Malabak.SharedKernel.Result Result(bool success = true) =>
            success ? Malabak.SharedKernel.Result.Success() : Malabak.SharedKernel.Result.Failure(BookingErrors.InvalidTransition);
    }
}
