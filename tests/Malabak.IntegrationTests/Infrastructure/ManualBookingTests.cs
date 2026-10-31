using FluentAssertions;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Bookings.ManualBooking;
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
/// A venue books a walk-in customer (TASK-037) against a real PostgreSQL: the pitch must belong to the venue the staff member
/// was authorised for, the actor must be a real user, and the slot is protected like any other booking's.
/// </summary>
public sealed class ManualBookingTests : IClassFixture<BookingDatabaseFixture>
{
    private static readonly DateTime _start = new(DateTime.UtcNow.Year + 5, 3, 1, 18, 0, 0, DateTimeKind.Utc);

    private readonly BookingDatabaseFixture _database;
    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();

    public ManualBookingTests(BookingDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task APitchOfTheVenue_IsBookedConfirmedWithTheStaffMemberAsActorAndTheNotesKeptAsync()
    {
        var actor = Guid.NewGuid();

        var result = await CreateAsync(Command(_start, actor: actor.ToString("D"), notes: "Paid cash"));

        result.IsSuccess.Should().BeTrue();
        await using var verify = _database.CreateContext();
        var booking = await verify.Bookings.SingleAsync(b => b.Id == result.Value.BookingId);
        booking.Status.Should().Be(BookingStatus.Confirmed);
        booking.VenueId.Should().Be(_venueId);
        booking.PlayerId.Should().Be(actor);
        booking.Notes.Should().Be("Paid cash");
    }

    [Fact]
    public async Task APitchOfAnotherVenue_LooksLikeOneThatDoesNotExist_AndNothingIsSavedAsync()
    {
        var foreignPitch = Guid.NewGuid();
        var directory = new FakeVenueDirectory().WithPitch(foreignPitch, Guid.NewGuid());

        var result = await CreateAsync(Command(_start.AddDays(1), pitchId: foreignPitch), directory);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BookingErrors.PitchNotFound);
        await using var verify = _database.CreateContext();
        (await verify.Bookings.AnyAsync(b => b.PitchId == foreignPitch)).Should().BeFalse();
    }

    [Fact]
    public async Task APitchThatDoesNotExistAtAll_IsRefusedTheSameWayAsync()
    {
        var result = await CreateAsync(Command(_start.AddDays(2), pitchId: Guid.NewGuid()));

        result.Error.Should().Be(BookingErrors.PitchNotFound);
    }

    [Fact]
    public async Task AnActorIdThatIsNotAUserId_IsRefused_NotReplacedWithARandomOneAsync()
    {
        var result = await CreateAsync(Command(_start.AddDays(3), actor: "not-a-guid"));

        result.Error.Should().Be(BookingErrors.UnauthorizedAccess);
    }

    [Fact]
    public async Task TheSameSlotTwice_ConflictsTheSecondTimeAsync()
    {
        (await CreateAsync(Command(_start.AddDays(4)))).IsSuccess.Should().BeTrue();

        var second = await CreateAsync(Command(_start.AddDays(4).AddMinutes(30)));

        second.IsFailure.Should().BeTrue();
        second.Error.Should().Be(BookingErrors.SlotOverlap);
    }

    private CreateManualBookingCommand Command(DateTime start, string? actor = null, Guid? pitchId = null, string? notes = null) => new(
        _venueId, pitchId ?? _pitchId, start, start.AddHours(1), 300m, "EGP", "Walk-in", "01000000000",
        actor ?? Guid.NewGuid().ToString("D"), notes);

    private async Task<Result<ManualBookingResponse>> CreateAsync(CreateManualBookingCommand command, IVenueDirectory? venues = null)
    {
        await using var context = _database.CreateContext();
        var handler = new CreateManualBookingHandler(
            context, new SystemClock(), new BookingReferenceGenerator(), new QuietInvalidator(), new QuietBroadcaster(),
            venues ?? new FakeVenueDirectory().WithPitch(_pitchId, _venueId), NullLogger<CreateManualBookingHandler>.Instance);
        return await handler.HandleAsync(command);
    }

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
}
