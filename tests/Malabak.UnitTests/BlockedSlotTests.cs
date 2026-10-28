using FluentAssertions;
using Malabak.Modules.Venues.Application.BlockedSlots.CreateBlockedSlot;
using Malabak.Modules.Venues.Application.BlockedSlots.DeleteBlockedSlot;
using Malabak.Modules.Venues.Application.BlockedSlots.GetBlockedSlotsByVenue;
using Malabak.Modules.Venues.Application.Common;
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

public sealed class BlockedSlotTests
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

    private static Venue CreateSampleVenue(Guid ownerId)
    {
        return Venue.Create(
            Guid.NewGuid(),
            "Al-Ahly Club",
            "alahly-club",
            "Historic sporting club",
            ownerId,
            "Gezira, Cairo",
            "Cairo",
            "Zamalek",
            30.0444,
            31.2357,
            "+201001234567",
            new[] { "Parking", "Locker Room" },
            new[] { "https://example.com/photo1.jpg" },
            new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc));
    }

    private static Pitch CreateSamplePitch(Guid venueId)
    {
        return Pitch.Create(
            Guid.NewGuid(),
            venueId,
            "Pitch 1",
            SurfaceType.ArtificialTurfValue,
            5,
            40m,
            20m,
            new[] { "https://example.com/pitch1.jpg" },
            new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc));
    }

    private BlockedSlot CreateSlot(Guid venueId, Guid pitchId, Guid userId, int daysAhead = 1, string reason = "Private event")
    {
        return BlockedSlot.Create(
            Guid.NewGuid(),
            venueId,
            pitchId,
            _clock.UtcNow.AddDays(daysAhead),
            _clock.UtcNow.AddDays(daysAhead).AddHours(2),
            reason,
            userId,
            _clock.UtcNow);
    }

    private CreateBlockedSlotHandler CreateHandler(
        VenuesDbContext dbContext,
        FakeVenueAuditLogger auditLogger,
        bool hasBookingConflict = false)
    {
        return new CreateBlockedSlotHandler(
            new VenueRepository(dbContext),
            new PitchRepository(dbContext),
            new BlockedSlotRepository(dbContext),
            new FakeBookingConflictChecker { HasConflict = hasBookingConflict },
            auditLogger,
            dbContext,
            _clock,
            NullLogger<CreateBlockedSlotHandler>.Instance);
    }

    private DeleteBlockedSlotHandler DeleteHandler(VenuesDbContext dbContext, FakeVenueAuditLogger auditLogger)
    {
        return new DeleteBlockedSlotHandler(
            new BlockedSlotRepository(dbContext),
            new VenueRepository(dbContext),
            auditLogger,
            dbContext,
            _clock,
            NullLogger<DeleteBlockedSlotHandler>.Instance);
    }

    private static async Task<(Venue Venue, Pitch Pitch)> SeedVenueWithPitchAsync(VenuesDbContext dbContext, Guid ownerId)
    {
        var venue = CreateSampleVenue(ownerId);
        var pitch = CreateSamplePitch(venue.Id);
        dbContext.Venues.Add(venue);
        dbContext.Pitches.Add(pitch);
        await dbContext.SaveChangesAsync();
        return (venue, pitch);
    }

    private CreateBlockedSlotCommand ValidCommand(Guid venueId, Guid pitchId, Guid userId)
    {
        var startUtc = _clock.UtcNow.AddDays(1);
        return new CreateBlockedSlotCommand(venueId, pitchId, userId, startUtc, startUtc.AddHours(2), "Maintenance");
    }

    [Fact]
    public void BlockedSlot_Create_ShouldInitializePropertiesCorrectly()
    {
        var id = Guid.NewGuid();
        var venueId = Guid.NewGuid();
        var pitchId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var startUtc = new DateTime(2026, 11, 1, 14, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2026, 11, 1, 16, 0, 0, DateTimeKind.Utc);

        var slot = BlockedSlot.Create(id, venueId, pitchId, startUtc, endUtc, "  Turf maintenance ", userId, _clock.UtcNow);

        slot.Id.Should().Be(id);
        slot.VenueId.Should().Be(venueId);
        slot.PitchId.Should().Be(pitchId);
        slot.StartUtc.Should().Be(startUtc);
        slot.EndUtc.Should().Be(endUtc);
        slot.Reason.Should().Be("Turf maintenance");
        slot.BlockedByUserId.Should().Be(userId);
        slot.CreatedAtUtc.Should().Be(_clock.UtcNow);
        slot.IsDeleted.Should().BeFalse();
        slot.UpdatedAtUtc.Should().BeNull();
    }

    [Fact]
    public void BlockedSlot_Create_ShouldThrow_WhenEndUtcBeforeStartUtc()
    {
        var startUtc = new DateTime(2026, 11, 1, 16, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2026, 11, 1, 14, 0, 0, DateTimeKind.Utc);

        var act = () => BlockedSlot.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), startUtc, endUtc, "Turf maintenance", Guid.NewGuid(), _clock.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("endUtc");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BlockedSlot_Create_ShouldThrow_WhenReasonIsInvalid(string? reason)
    {
        var startUtc = new DateTime(2026, 11, 1, 14, 0, 0, DateTimeKind.Utc);

        var act = () => BlockedSlot.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), startUtc, startUtc.AddHours(2), reason!, Guid.NewGuid(), _clock.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BlockedSlot_MarkDeleted_ShouldSetIsDeletedAndUpdatedAtUtc()
    {
        var slot = CreateSlot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var deletedAt = _clock.UtcNow.AddHours(1);

        slot.MarkDeleted(deletedAt);

        slot.IsDeleted.Should().BeTrue();
        slot.UpdatedAtUtc.Should().Be(deletedAt);
    }

    [Fact]
    public async Task CreateBlockedSlotHandler_VenueDoesNotExist_ReturnsVenueNotFoundAsync()
    {
        await using var dbContext = CreateDbContext();
        var handler = CreateHandler(dbContext, new FakeVenueAuditLogger());

        var result = await handler.HandleAsync(ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Venue.NotFound");
    }

    [Fact]
    public async Task CreateBlockedSlotHandler_UserIsNotOwner_ReturnsVenueForbiddenAsync()
    {
        await using var dbContext = CreateDbContext();
        var (venue, pitch) = await SeedVenueWithPitchAsync(dbContext, Guid.NewGuid());
        var handler = CreateHandler(dbContext, new FakeVenueAuditLogger());

        var result = await handler.HandleAsync(ValidCommand(venue.Id, pitch.Id, Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Venue.Forbidden");
    }

    [Fact]
    public async Task CreateBlockedSlotHandler_PitchBelongsToAnotherVenue_ReturnsPitchNotFoundAsync()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var (venue, _) = await SeedVenueWithPitchAsync(dbContext, ownerId);
        var (_, otherVenuePitch) = await SeedVenueWithPitchAsync(dbContext, Guid.NewGuid());
        var handler = CreateHandler(dbContext, new FakeVenueAuditLogger());

        var result = await handler.HandleAsync(ValidCommand(venue.Id, otherVenuePitch.Id, ownerId));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Pitch.NotFound");
    }

    [Fact]
    public async Task CreateBlockedSlotHandler_OverlappingBlockExists_ReturnsOverlapAsync()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var (venue, pitch) = await SeedVenueWithPitchAsync(dbContext, ownerId);
        var existing = CreateSlot(venue.Id, pitch.Id, ownerId);
        dbContext.BlockedSlots.Add(existing);
        await dbContext.SaveChangesAsync();
        var handler = CreateHandler(dbContext, new FakeVenueAuditLogger());

        var command = new CreateBlockedSlotCommand(
            venue.Id, pitch.Id, ownerId, existing.StartUtc.AddMinutes(30), existing.EndUtc.AddMinutes(30), "New maintenance");
        var result = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BlockedSlot.Overlap");
    }

    [Fact]
    public async Task CreateBlockedSlotHandler_AdjacentToExistingBlock_SucceedsAsync()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var (venue, pitch) = await SeedVenueWithPitchAsync(dbContext, ownerId);
        var existing = CreateSlot(venue.Id, pitch.Id, ownerId);
        dbContext.BlockedSlots.Add(existing);
        await dbContext.SaveChangesAsync();
        var handler = CreateHandler(dbContext, new FakeVenueAuditLogger());

        var command = new CreateBlockedSlotCommand(
            venue.Id, pitch.Id, ownerId, existing.EndUtc, existing.EndUtc.AddHours(1), "Back-to-back block");
        var result = await handler.HandleAsync(command);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task CreateBlockedSlotHandler_ActiveBookingsExist_ReturnsConflictingBookingsAsync()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var (venue, pitch) = await SeedVenueWithPitchAsync(dbContext, ownerId);
        var handler = CreateHandler(dbContext, new FakeVenueAuditLogger(), hasBookingConflict: true);

        var result = await handler.HandleAsync(ValidCommand(venue.Id, pitch.Id, ownerId));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BlockedSlot.ConflictingBookings");
        (await dbContext.BlockedSlots.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateBlockedSlotHandler_WindowAlreadyEnded_ReturnsInPastAsync()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var (venue, pitch) = await SeedVenueWithPitchAsync(dbContext, ownerId);
        var handler = CreateHandler(dbContext, new FakeVenueAuditLogger());

        var command = new CreateBlockedSlotCommand(
            venue.Id, pitch.Id, ownerId, _clock.UtcNow.AddHours(-3), _clock.UtcNow.AddHours(-1), "Too late");
        var result = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BlockedSlot.InPast");
    }

    [Fact]
    public async Task CreateBlockedSlotHandler_EndBeforeStart_ReturnsInvalidTimeInsteadOfThrowingAsync()
    {
        await using var dbContext = CreateDbContext();
        var handler = CreateHandler(dbContext, new FakeVenueAuditLogger());
        var startUtc = _clock.UtcNow.AddDays(1);

        var command = new CreateBlockedSlotCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), startUtc, startUtc.AddHours(-1), "Maintenance");
        var result = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BlockedSlot.InvalidTime");
    }

    [Fact]
    public async Task CreateBlockedSlotHandler_NonUtcTimestamps_ReturnsInvalidTimeAsync()
    {
        await using var dbContext = CreateDbContext();
        var handler = CreateHandler(dbContext, new FakeVenueAuditLogger());
        var startLocal = DateTime.SpecifyKind(_clock.UtcNow.AddDays(1), DateTimeKind.Unspecified);

        var command = new CreateBlockedSlotCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), startLocal, startLocal.AddHours(2), "Maintenance");
        var result = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BlockedSlot.InvalidTime");
    }

    [Fact]
    public async Task CreateBlockedSlotHandler_ReasonTooLong_ReturnsInvalidReasonAsync()
    {
        await using var dbContext = CreateDbContext();
        var handler = CreateHandler(dbContext, new FakeVenueAuditLogger());
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) with { Reason = new string('x', 501) };

        var result = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BlockedSlot.InvalidReason");
    }

    [Fact]
    public async Task CreateBlockedSlotHandler_ValidRequest_PersistsSlotAndWritesAuditWithoutReasonAsync()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var (venue, pitch) = await SeedVenueWithPitchAsync(dbContext, ownerId);
        var auditLogger = new FakeVenueAuditLogger();
        var handler = CreateHandler(dbContext, auditLogger);

        var startUtc = _clock.UtcNow.AddDays(1);
        var endUtc = startUtc.AddHours(3);
        var command = new CreateBlockedSlotCommand(venue.Id, pitch.Id, ownerId, startUtc, endUtc, "Full pitch grass renovation");

        var result = await handler.HandleAsync(command);

        result.IsSuccess.Should().BeTrue();
        result.Value.PitchId.Should().Be(pitch.Id);
        result.Value.VenueId.Should().Be(venue.Id);
        result.Value.StartUtc.Should().Be(startUtc);
        result.Value.EndUtc.Should().Be(endUtc);
        result.Value.Reason.Should().Be("Full pitch grass renovation");

        var savedSlot = await dbContext.BlockedSlots.SingleAsync(b => b.Id == result.Value.Id);
        savedSlot.Reason.Should().Be("Full pitch grass renovation");

        auditLogger.LoggedEvents.Should().ContainSingle();
        auditLogger.LoggedEvents[0].Action.Should().Be("BlockedSlot.Created");
        auditLogger.LoggedEvents[0].EntityId.Should().Be(result.Value.Id);
        auditLogger.LoggedEvents[0].Metadata!.GetType().GetProperty("Reason").Should().BeNull();
    }

    [Fact]
    public async Task DeleteBlockedSlotHandler_SlotDoesNotExist_ReturnsNotFoundAsync()
    {
        await using var dbContext = CreateDbContext();
        var handler = DeleteHandler(dbContext, new FakeVenueAuditLogger());

        var result = await handler.HandleAsync(new DeleteBlockedSlotCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BlockedSlot.NotFound");
    }

    [Fact]
    public async Task DeleteBlockedSlotHandler_SlotAddressedThroughOtherVenueRoute_ReturnsNotFoundAsync()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var (venue, pitch) = await SeedVenueWithPitchAsync(dbContext, ownerId);
        var (otherVenue, _) = await SeedVenueWithPitchAsync(dbContext, ownerId);
        var slot = CreateSlot(venue.Id, pitch.Id, ownerId);
        dbContext.BlockedSlots.Add(slot);
        await dbContext.SaveChangesAsync();
        var handler = DeleteHandler(dbContext, new FakeVenueAuditLogger());

        var result = await handler.HandleAsync(new DeleteBlockedSlotCommand(otherVenue.Id, slot.Id, ownerId));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BlockedSlot.NotFound");
        (await dbContext.BlockedSlots.SingleAsync(b => b.Id == slot.Id)).IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteBlockedSlotHandler_UserIsNotOwner_ReturnsForbiddenAsync()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var (venue, pitch) = await SeedVenueWithPitchAsync(dbContext, ownerId);
        var slot = CreateSlot(venue.Id, pitch.Id, ownerId);
        dbContext.BlockedSlots.Add(slot);
        await dbContext.SaveChangesAsync();
        var handler = DeleteHandler(dbContext, new FakeVenueAuditLogger());

        var result = await handler.HandleAsync(new DeleteBlockedSlotCommand(venue.Id, slot.Id, Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BlockedSlot.Forbidden");
    }

    [Fact]
    public async Task DeleteBlockedSlotHandler_ValidRequest_SoftDeletesAndWritesAuditAsync()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var (venue, pitch) = await SeedVenueWithPitchAsync(dbContext, ownerId);
        var slot = CreateSlot(venue.Id, pitch.Id, ownerId);
        dbContext.BlockedSlots.Add(slot);
        await dbContext.SaveChangesAsync();
        var auditLogger = new FakeVenueAuditLogger();
        var handler = DeleteHandler(dbContext, auditLogger);

        var result = await handler.HandleAsync(new DeleteBlockedSlotCommand(venue.Id, slot.Id, ownerId));

        result.IsSuccess.Should().BeTrue();

        // The row must still exist: removal is a soft delete so the block history is kept.
        var slotInDb = await dbContext.BlockedSlots.SingleOrDefaultAsync(b => b.Id == slot.Id);
        slotInDb.Should().NotBeNull();
        slotInDb!.IsDeleted.Should().BeTrue();
        slotInDb.UpdatedAtUtc.Should().Be(_clock.UtcNow);

        auditLogger.LoggedEvents.Should().ContainSingle();
        auditLogger.LoggedEvents[0].Action.Should().Be("BlockedSlot.Deleted");
        auditLogger.LoggedEvents[0].EntityId.Should().Be(slot.Id);
    }

    [Fact]
    public async Task GetBlockedSlotsByVenueHandler_OwnerRequests_ReturnsActiveSlotsOfThatVenueOnlyAsync()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var (venue, pitch) = await SeedVenueWithPitchAsync(dbContext, ownerId);
        var (otherVenue, otherPitch) = await SeedVenueWithPitchAsync(dbContext, Guid.NewGuid());

        var activeSlot1 = CreateSlot(venue.Id, pitch.Id, ownerId, daysAhead: 1);
        var activeSlot2 = CreateSlot(venue.Id, pitch.Id, ownerId, daysAhead: 2);
        var deletedSlot = CreateSlot(venue.Id, pitch.Id, ownerId, daysAhead: 3);
        deletedSlot.MarkDeleted(_clock.UtcNow);
        var otherVenueSlot = CreateSlot(otherVenue.Id, otherPitch.Id, otherVenue.OwnerId);

        dbContext.BlockedSlots.AddRange(activeSlot1, activeSlot2, deletedSlot, otherVenueSlot);
        await dbContext.SaveChangesAsync();

        var handler = new GetBlockedSlotsByVenueHandler(new VenueAccessService(dbContext), new BlockedSlotRepository(dbContext));
        var result = await handler.HandleAsync(new GetBlockedSlotsByVenueQuery(venue.Id, ownerId));

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(s => s.Id).Should().BeEquivalentTo(new[] { activeSlot1.Id, activeSlot2.Id });
    }

    [Fact]
    public async Task GetBlockedSlotsByVenueHandler_UserIsNotOwner_ReturnsForbiddenAsync()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var (venue, pitch) = await SeedVenueWithPitchAsync(dbContext, ownerId);
        dbContext.BlockedSlots.Add(CreateSlot(venue.Id, pitch.Id, ownerId));
        await dbContext.SaveChangesAsync();

        var handler = new GetBlockedSlotsByVenueHandler(new VenueAccessService(dbContext), new BlockedSlotRepository(dbContext));
        var result = await handler.HandleAsync(new GetBlockedSlotsByVenueQuery(venue.Id, Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Venue.NotFound");
    }

    private sealed class FakeBookingConflictChecker : IBookingConflictChecker
    {
        public bool HasConflict { get; set; }

        public Task<bool> HasActiveBookingsAsync(
            Guid pitchId,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(HasConflict);
        }
    }

    private sealed class FakeVenueAuditLogger : IVenueAuditLogger
    {
        public List<(string Action, string EntityName, Guid EntityId, Guid UserId, object? Metadata)> LoggedEvents { get; } = new();

        public Task LogAsync(
            string action,
            string entityName,
            Guid entityId,
            Guid userId,
            object? metadata = null,
            CancellationToken cancellationToken = default)
        {
            LoggedEvents.Add((action, entityName, entityId, userId, metadata));
            return Task.CompletedTask;
        }
    }

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
