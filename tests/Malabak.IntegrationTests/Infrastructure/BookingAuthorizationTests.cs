using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure.Builders;
using Malabak.Modules.Booking.Application.Bookings.Proofs;
using Malabak.Modules.Booking.Application.Bookings.PlayerQueries;
using Malabak.Modules.Booking.Domain;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// Who may read or change a booking, against a real database: a booking and its payment receipts belong to their player
/// (and, through venue-scoped endpoints, to the venue's staff), and nobody else.
/// </summary>
public sealed class BookingAuthorizationTests : IClassFixture<BookingDatabaseFixture>
{
    private static readonly DateTime _start = new(DateTime.UtcNow.Year + 3, 5, 1, 18, 0, 0, DateTimeKind.Utc);

    private readonly BookingDatabaseFixture _database;

    public BookingAuthorizationTests(BookingDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task PaymentProofs_AreReadableByTheBookingsPlayerAsync()
    {
        var (bookingId, playerId) = await SaveBookingAsync();
        await using var context = _database.CreateContext();

        var result = await new GetBookingProofsHandler(context).HandleAsync(new GetBookingProofsQuery(bookingId, playerId));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PaymentProofs_AreHiddenFromEveryoneElseAsync()
    {
        var (bookingId, _) = await SaveBookingAsync();
        await using var context = _database.CreateContext();

        var result = await new GetBookingProofsHandler(context).HandleAsync(new GetBookingProofsQuery(bookingId, Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BookingErrors.NotFound, "a booking that is not yours must look like one that does not exist");
    }

    [Fact]
    public async Task ABookingDetail_IsHiddenFromOtherPlayersAsync()
    {
        var (bookingId, playerId) = await SaveBookingAsync();
        await using var context = _database.CreateContext();
        var handler = new PlayerBookingQueriesHandler(context, new Malabak.SharedKernel.Time.FakeClock(), new FakeVenueDirectory());

        var own = await handler.HandleAsync(new GetPlayerBookingByIdQuery(bookingId, playerId));
        var foreign = await handler.HandleAsync(new GetPlayerBookingByIdQuery(bookingId, Guid.NewGuid()));

        own.IsSuccess.Should().BeTrue();
        own.Value.Venue.Should().NotBeNull("the confirmation screen needs the venue's name and where it is");
        own.Value.Venue!.Latitude.Should().NotBeNull();
        own.Value.PitchName.Should().Be(FakeVenueDirectory.PitchName);
        own.Value.VenueCoverPhotoUrl.Should().Be(FakeVenueDirectory.CoverPhoto);
        foreign.IsFailure.Should().BeTrue();
        foreign.Error.Should().Be(BookingErrors.NotFound, "a booking that is not yours must look like one that does not exist");
    }

    [Fact]
    public async Task MyBookings_NameTheVenueAndPitchAndShowItsPhotoAsync()
    {
        var (_, playerId) = await SaveBookingAsync();
        await using var context = _database.CreateContext();
        var handler = new PlayerBookingQueriesHandler(context, new Malabak.SharedKernel.Time.FakeClock(), new FakeVenueDirectory());

        var result = await handler.HandleAsync(new GetPlayerBookingsQuery(playerId, "all"));

        result.IsSuccess.Should().BeTrue();
        var item = result.Value.Items.Should().ContainSingle().Subject;
        item.VenueName.Should().Be(FakeVenueDirectory.VenueName);
        item.PitchName.Should().Be(FakeVenueDirectory.PitchName);
        item.VenueCoverPhotoUrl.Should().Be(FakeVenueDirectory.CoverPhoto);
    }

    private async Task<(Guid BookingId, Guid PlayerId)> SaveBookingAsync()
    {
        var playerId = Guid.NewGuid();
        var booking = new BookingBuilder().ForPlayer(playerId).WithTimeSlot(_start, _start.AddHours(1)).Build();
        await using var context = _database.CreateContext();
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
        return (booking.Id, playerId);
    }
}
