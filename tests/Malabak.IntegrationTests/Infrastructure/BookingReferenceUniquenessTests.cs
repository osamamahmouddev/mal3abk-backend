using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure.Builders;
using Malabak.Modules.Booking.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// The generator is random, so uniqueness is the database's job (rule.md section 5): the unique index rejects a
/// duplicate reference, and the helper the booking flow uses to retry must recognise exactly that error.
/// </summary>
public sealed class BookingReferenceUniquenessTests : IClassFixture<BookingDatabaseFixture>
{
    private static readonly DateTime _start = new(DateTime.UtcNow.Year + 2, 3, 1, 18, 0, 0, DateTimeKind.Utc);

    private readonly BookingDatabaseFixture _database;

    public BookingReferenceUniquenessTests(BookingDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task DuplicateReference_IsRejectedAndRecognisedAsACollisionAsync()
    {
        const string reference = "MLB-DUPE22";
        await SaveAsync(reference, Guid.NewGuid(), hourOffset: 0);

        var duplicate = async () => await SaveAsync(reference, Guid.NewGuid(), hourOffset: 0);

        var failure = await duplicate.Should().ThrowAsync<DbUpdateException>();
        PostgresErrors.IsReferenceCollision(failure.Which).Should().BeTrue();
        PostgresErrors.IsSlotOverlap(failure.Which).Should().BeFalse();
    }

    [Fact]
    public async Task OverlapOnADifferentReference_IsNotMistakenForACollisionAsync()
    {
        var pitchId = Guid.NewGuid();
        await SaveAsync("MLB-AAAAAA", pitchId, hourOffset: 0);

        var overlapping = async () => await SaveAsync("MLB-BBBBBB", pitchId, hourOffset: 0);

        var failure = await overlapping.Should().ThrowAsync<DbUpdateException>();
        PostgresErrors.IsSlotOverlap(failure.Which).Should().BeTrue();
        PostgresErrors.IsReferenceCollision(failure.Which).Should().BeFalse("retrying with a new reference cannot fix an overlap");
    }

    private async Task SaveAsync(string reference, Guid pitchId, int hourOffset)
    {
        var booking = new BookingBuilder()
            .WithBookingReference(reference)
            .ForPitch(pitchId)
            .WithTimeSlot(_start.AddHours(hourOffset), _start.AddHours(hourOffset + 1))
            .Build();

        await using var context = _database.CreateContext();
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
    }
}
