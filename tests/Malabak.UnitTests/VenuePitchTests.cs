using FluentAssertions;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Pitches.CreatePitch;
using Malabak.Modules.Venues.Application.Pitches.DeletePitch;
using Malabak.Modules.Venues.Application.Pitches.GetPitchById;
using Malabak.Modules.Venues.Application.Pitches.GetPitchesByVenue;
using Malabak.Modules.Venues.Application.Pitches.UpdatePitch;
using Malabak.Modules.Venues.Application.Venues.CreateVenue;
using Malabak.Modules.Venues.Application.Venues.DeleteVenue;
using Malabak.Modules.Venues.Application.Venues.GetVenueById;
using Malabak.Modules.Venues.Application.Venues.GetVenuesByOwner;
using Malabak.Modules.Venues.Application.Venues.PublishVenue;
using Malabak.Modules.Venues.Application.Venues.UpdateVenue;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.Modules.Venues.Infrastructure.Repositories;
using Malabak.Modules.Venues.Infrastructure.Services;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class VenuePitchTests
{
    private readonly IClock _clock = new TestClock();

    private static VenuesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<VenuesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new VenuesDbContext(options);
    }

    [Fact]
    public void PitchCapacity_Create_ValidValues_ShouldSucceed()
    {
        // 5v5
        var cap5 = PitchCapacity.Create(5);
        cap5.IsSuccess.Should().BeTrue();
        cap5.Value.PlayersPerTeam.Should().Be(5);
        cap5.Value.TotalPlayers.Should().Be(10);
        cap5.Value.Format.Should().Be("5x5");

        // 7v7
        var cap7 = PitchCapacity.Create(7);
        cap7.IsSuccess.Should().BeTrue();
        cap7.Value.PlayersPerTeam.Should().Be(7);
        cap7.Value.TotalPlayers.Should().Be(14);
        cap7.Value.Format.Should().Be("7x7");

        // 11v11
        var cap11 = PitchCapacity.Create(11);
        cap11.IsSuccess.Should().BeTrue();
        cap11.Value.PlayersPerTeam.Should().Be(11);
        cap11.Value.TotalPlayers.Should().Be(22);
        cap11.Value.Format.Should().Be("11x11");
    }

    [Fact]
    public void PitchCapacity_Create_InvalidValues_ShouldFail()
    {
        PitchCapacity.Create(2).IsFailure.Should().BeTrue();
        PitchCapacity.Create(12).IsFailure.Should().BeTrue();
        PitchCapacity.FromTotalPlayers(7).IsFailure.Should().BeTrue();
        PitchCapacity.FromTotalPlayers(4).IsFailure.Should().BeTrue();
        PitchCapacity.FromTotalPlayers(24).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void SurfaceType_Create_ValidTypes_ShouldSucceed()
    {
        var validTypes = new[]
        {
            SurfaceType.ArtificialTurfValue,
            SurfaceType.NaturalGrassValue,
            SurfaceType.HybridGrassValue,
            SurfaceType.TartanValue,
            SurfaceType.HardCourtValue,
            SurfaceType.ParquetValue,
            SurfaceType.SandValue
        };

        foreach (var type in validTypes)
        {
            var result = SurfaceType.Create(type);
            result.IsSuccess.Should().BeTrue();
            result.Value.DisplayName.Should().NotBeNullOrWhiteSpace();
            result.Value.ArabicDisplayName.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void SurfaceType_Create_InvalidType_ShouldFail()
    {
        var result = SurfaceType.Create("Asphalt");
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Pitch.InvalidSurfaceType");
    }

    [Fact]
    public void Venue_Coordinates_Validation_ShouldThrowWhenOutOfRange()
    {
        var actLat = () => Venue.Create(
            Guid.NewGuid(), "Venue", "slug", null, Guid.NewGuid(),
            "Addr", "Cairo", "Area", 95.0, 31.0, "+201000000000",
            null, null, _clock.UtcNow);

        actLat.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Latitude must be between -90 and 90 degrees*");

        var actLon = () => Venue.Create(
            Guid.NewGuid(), "Venue", "slug", null, Guid.NewGuid(),
            "Addr", "Cairo", "Area", 30.0, 185.0, "+201000000000",
            null, null, _clock.UtcNow);

        actLon.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Longitude must be between -180 and 180 degrees*");
    }

    [Fact]
    public void Pitch_Create_ThrowsWhenDimensionsNonPositive()
    {
        var actLength = () => Pitch.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Pitch A", SurfaceType.ArtificialTurfValue,
            5, -10m, 20m, null, _clock.UtcNow);

        actLength.Should().Throw<ArgumentException>()
            .WithMessage("*Length must be greater than zero*");

        var actWidth = () => Pitch.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Pitch A", SurfaceType.ArtificialTurfValue,
            5, 40m, 0m, null, _clock.UtcNow);

        actWidth.Should().Throw<ArgumentException>()
            .WithMessage("*Width must be greater than zero*");
    }

    [Fact]
    public async Task CreateVenueHandler_CreatesVenue_WithAutoSlugAsync()
    {
        using var dbContext = CreateDbContext();
        var venueRepo = new VenueRepository(dbContext);
        var handler = new CreateVenueHandler(venueRepo, dbContext, new AllowAllEntitlements(), _clock, NullLogger<CreateVenueHandler>.Instance);

        var ownerId = Guid.NewGuid();
        var command = new CreateVenueCommand(
            ownerId,
            "Al Ahly Arena",
            "Premier football facility",
            "Nasr City St",
            "Cairo",
            "Nasr City",
            30.05,
            31.35,
            "+201011112222",
            ["Parking", "Showers"],
            ["https://img.mal3abk.com/ahly.jpg"]);

        var result = await handler.HandleAsync(command);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Al Ahly Arena");
        result.Value.Slug.Should().Be("al-ahly-arena");
        result.Value.IsApproved.Should().BeFalse();
        result.Value.IsPublished.Should().BeFalse();

        // Adding second venue with same name creates incremental slug
        var result2 = await handler.HandleAsync(command);
        result2.IsSuccess.Should().BeTrue();
        result2.Value.Slug.Should().Be("al-ahly-arena-1");
    }

    [Fact]
    public async Task UpdateVenueHandler_Rejects_WhenNotOwnerAsync()
    {
        using var dbContext = CreateDbContext();
        var venueRepo = new VenueRepository(dbContext);

        var owner1 = Guid.NewGuid();
        var owner2 = Guid.NewGuid();

        var venue = Venue.Create(
            Guid.NewGuid(), "Zamalek Arena", "zamalek-arena", null,
            owner1, "Mohandessin", "Giza", "Mohandessin",
            null, null, "+201011113333", null, null, _clock.UtcNow);

        venueRepo.Add(venue);
        await dbContext.SaveChangesAsync();

        var handler = new UpdateVenueHandler(venueRepo, dbContext, _clock, NullLogger<UpdateVenueHandler>.Instance);

        var command = new UpdateVenueCommand(
            venue.Id,
            owner2, // Different owner
            "Updated Arena",
            null,
            "Mohandessin",
            "Giza",
            "Mohandessin",
            null,
            null,
            "+201011113333",
            null,
            null);

        var result = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Venue.Forbidden");
    }

    [Fact]
    public async Task PublishVenueHandler_FailsIfNotApproved_SucceedsIfApprovedAsync()
    {
        using var dbContext = CreateDbContext();
        var venueRepo = new VenueRepository(dbContext);

        var ownerId = Guid.NewGuid();
        var venue = Venue.Create(
            Guid.NewGuid(), "Shooting Club", "shooting-club", null,
            ownerId, "Dokki", "Giza", "Dokki",
            null, null, "+201011114444", null, null, _clock.UtcNow);

        venueRepo.Add(venue);
        await dbContext.SaveChangesAsync();

        var handler = new PublishVenueHandler(venueRepo, dbContext, _clock, NullLogger<PublishVenueHandler>.Instance);

        // Attempt publish before approval -> fails
        var publishResult = await handler.HandleAsync(new PublishVenueCommand(venue.Id, ownerId, true));
        publishResult.IsFailure.Should().BeTrue();
        publishResult.Error.Code.Should().Be("Venue.NotApproved");

        // Approve venue
        venue.SubmitForApproval(ownerId, null, _clock.UtcNow).IsSuccess.Should().BeTrue();
        venue.Approve(Guid.NewGuid(), null, _clock.UtcNow).IsSuccess.Should().BeTrue();
        await dbContext.SaveChangesAsync();

        // Publish now succeeds
        var approvedPublishResult = await handler.HandleAsync(new PublishVenueCommand(venue.Id, ownerId, true));
        approvedPublishResult.IsSuccess.Should().BeTrue();

        var updatedVenue = await venueRepo.GetByIdAsync(venue.Id);
        updatedVenue!.IsPublished.Should().BeTrue();
    }

    [Fact]
    public async Task PitchCrudHandlers_ManagePitches_SuccessfullyAsync()
    {
        using var dbContext = CreateDbContext();
        var venueRepo = new VenueRepository(dbContext);
        var pitchRepo = new PitchRepository(dbContext);

        var ownerId = Guid.NewGuid();
        var venue = Venue.Create(
            Guid.NewGuid(), "Sports Hub", "sports-hub", null,
            ownerId, "Maadi", "Cairo", "Maadi",
            null, null, "+201011115555", null, null, _clock.UtcNow);

        venueRepo.Add(venue);
        await dbContext.SaveChangesAsync();

        // 1. Create Pitch
        var createPitchHandler = new CreatePitchHandler(venueRepo, pitchRepo, dbContext, new AllowAllEntitlements(), _clock, NullLogger<CreatePitchHandler>.Instance);
        var createCommand = new CreatePitchCommand(
            venue.Id,
            ownerId,
            "Main Pitch",
            SurfaceType.ArtificialTurfValue,
            5,
            40m,
            20m,
            ["https://img.mal3abk.com/pitch1.jpg"]);

        var createResult = await createPitchHandler.HandleAsync(createCommand);
        createResult.IsSuccess.Should().BeTrue();
        var pitchId = createResult.Value.Id;
        createResult.Value.Capacity.Should().Be(5);
        createResult.Value.Format.Should().Be("5x5");

        // 2. Query Pitch By Id
        var accessService = new VenueAccessService(dbContext);
        var getPitchHandler = new GetPitchByIdHandler(pitchRepo, accessService);
        var getResult = await getPitchHandler.HandleAsync(new GetPitchByIdQuery(pitchId, ownerId));
        getResult.IsSuccess.Should().BeTrue();
        getResult.Value.Name.Should().Be("Main Pitch");

        // 3. Query Pitches By Venue
        var getPitchesByVenueHandler = new GetPitchesByVenueHandler(pitchRepo, accessService);
        var pitchesResult = await getPitchesByVenueHandler.HandleAsync(new GetPitchesByVenueQuery(venue.Id, ownerId));
        pitchesResult.IsSuccess.Should().BeTrue();
        pitchesResult.Value.Should().HaveCount(1);

        // 4. Update Pitch
        var updatePitchHandler = new UpdatePitchHandler(pitchRepo, venueRepo, dbContext, _clock, NullLogger<UpdatePitchHandler>.Instance);
        var updateCommand = new UpdatePitchCommand(
            pitchId,
            ownerId,
            "Main Pitch Updated",
            SurfaceType.HybridGrassValue,
            7, // 7v7
            60m,
            40m,
            ["https://img.mal3abk.com/pitch1_new.jpg"]);

        var updateResult = await updatePitchHandler.HandleAsync(updateCommand);
        updateResult.IsSuccess.Should().BeTrue();
        updateResult.Value.Name.Should().Be("Main Pitch Updated");
        updateResult.Value.Capacity.Should().Be(7);
        updateResult.Value.Format.Should().Be("7x7");

        // 5. Delete Pitch
        var deletePitchHandler = new DeletePitchHandler(pitchRepo, venueRepo, dbContext, _clock, NullLogger<DeletePitchHandler>.Instance);
        var deleteResult = await deletePitchHandler.HandleAsync(new DeletePitchCommand(pitchId, ownerId));
        deleteResult.IsSuccess.Should().BeTrue();

        // Pitch should no longer be returned by repo
        var deletedPitch = await pitchRepo.GetByIdAsync(pitchId);
        deletedPitch.Should().BeNull();
    }

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
