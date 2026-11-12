using FluentAssertions;
using Malabak.Modules.Venues.Application.BlockedSlots;
using Malabak.Modules.Venues.Application.BlockedSlots.CreateBlockedSlot;
using Malabak.Modules.Venues.Application.BlockedSlots.DeleteBlockedSlot;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Schedules.UpdateVenueSchedules;
using Malabak.Modules.Venues.Application.SpecialSchedules;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.Modules.Venues.Infrastructure.Repositories;
using Malabak.Modules.Venues.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Edge-case test suite for Venue Weekly Schedules, Overnight Slots, Cairo DST Transitions,
/// Schedule Change Impact Warnings, and Blocked Slots (P3).
/// </summary>
public sealed class VenueScheduleEdgeCaseTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    private static readonly DateTime _now = new(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly FixedClock _clock = new();

    private static VenuesDbContext CreateVenuesDb()
    {
        var options = new DbContextOptionsBuilder<VenuesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new VenuesDbContext(options);
    }

    [Fact]
    public void UpdateVenueSchedulesValidator_RejectsOverlappingDaysAndEqualOpenClose()
    {
        var validator = new UpdateVenueSchedulesValidator();

        // 1. Duplicate entries for the same day
        var dupCommand = new UpdateVenueSchedulesCommand(
            _venueId, _ownerId,
            [
                new ScheduleItemInput(DayOfWeek.Sunday, new TimeOnly(10, 0), new TimeOnly(14, 0), 60, true),
                new ScheduleItemInput(DayOfWeek.Sunday, new TimeOnly(16, 0), new TimeOnly(22, 0), 60, true)
            ]);
        var dupResult = validator.Validate(dupCommand);
        dupResult.IsValid.Should().BeFalse();
        dupResult.Errors.Should().Contain(e => e.ErrorMessage.Contains("Each day of the week can only have one schedule entry"));

        // 2. Open == Close
        var equalCommand = new UpdateVenueSchedulesCommand(
            _venueId, _ownerId,
            [
                new ScheduleItemInput(DayOfWeek.Monday, new TimeOnly(12, 0), new TimeOnly(12, 0), 60, true)
            ]);
        var equalResult = validator.Validate(equalCommand);
        equalResult.IsValid.Should().BeFalse();
        equalResult.Errors.Should().Contain(e => e.ErrorMessage.Contains("Open time and close time cannot be equal"));

        // 3. Slot duration < 15 or > 360
        var durationCommand = new UpdateVenueSchedulesCommand(
            _venueId, _ownerId,
            [
                new ScheduleItemInput(DayOfWeek.Tuesday, new TimeOnly(10, 0), new TimeOnly(18, 0), 10, true)
            ]);
        var durationResult = validator.Validate(durationCommand);
        durationResult.IsValid.Should().BeFalse();
        durationResult.Errors.Should().Contain(e => e.ErrorMessage.Contains("Slot duration must be between 15 and 360 minutes"));
    }

    [Fact]
    public void OvernightSchedule_1800To0200_GeneratesCorrectSlotsAcrossMidnight()
    {
        var schedule = OperatingSchedule.Create(
            Guid.NewGuid(), _venueId, DayOfWeek.Thursday,
            new TimeOnly(18, 0), new TimeOnly(2, 0), 60, _now);

        schedule.IsOvernight.Should().BeTrue();
        schedule.TotalDurationMinutes.Should().Be(480, "6 hours before midnight (18-24) + 2 hours after midnight (00-02) = 8 hours");

        var businessDate = new DateOnly(2026, 11, 12); // Thursday
        var slots = schedule.GenerateSlots(businessDate, _cairo);

        slots.Should().HaveCount(8);

        // Pre-midnight slot: 18:00 - 19:00
        var slot0 = slots[0];
        slot0.BusinessDate.Should().Be(businessDate);
        slot0.StartTimeCairo.Should().Be(new DateTime(2026, 11, 12, 18, 0, 0, DateTimeKind.Unspecified));
        slot0.EndTimeCairo.Should().Be(new DateTime(2026, 11, 12, 19, 0, 0, DateTimeKind.Unspecified));
        slot0.IsOvernight.Should().BeFalse();

        // Post-midnight slot: 01:00 - 02:00
        var slot7 = slots[7];
        slot7.BusinessDate.Should().Be(businessDate, "Must retain original business date");
        slot7.StartTimeCairo.Should().Be(new DateTime(2026, 11, 13, 1, 0, 0, DateTimeKind.Unspecified));
        slot7.EndTimeCairo.Should().Be(new DateTime(2026, 11, 13, 2, 0, 0, DateTimeKind.Unspecified));
        slot7.IsOvernight.Should().BeTrue("Slot is after midnight");
    }

    [Fact]
    public void AfricaCairo_DstTransitions_AdjustOpenWindowsCorrectly()
    {
        var schedule = OperatingSchedule.Create(
            Guid.NewGuid(), _venueId, DayOfWeek.Thursday,
            new TimeOnly(18, 0), new TimeOnly(2, 0), 60, _now);
        var state = new PitchScheduleState(_pitchId, [schedule], [], [], BookingRules.Defaults(_venueId));

        // Spring forward: 18:00 to 02:00 skips the missing hour (00:00-01:00), making the window 7 hours instead of 8
        var springForwardDate = new DateOnly(2026, 4, 23);
        var expectedSpringHours = _cairo.IsInvalidTime(new DateTime(2026, 4, 24, 0, 30, 0, DateTimeKind.Unspecified)) ? 7 : 8;
        var springWindows = AvailabilityCalculator.OpenWindows(state, springForwardDate, _cairo);
        (springWindows[0].EndUtc - springWindows[0].StartUtc).Should().Be(TimeSpan.FromHours(expectedSpringHours));

        // Fall back: 18:00 to 02:00 includes the repeated hour (23:00-00:00), making the window 9 hours instead of 8
        var fallBackDate = new DateOnly(2026, 10, 29);
        var expectedFallHours = _cairo.IsAmbiguousTime(new DateTime(2026, 10, 29, 23, 30, 0, DateTimeKind.Unspecified)) ? 9 : 8;
        var fallWindows = AvailabilityCalculator.OpenWindows(state, fallBackDate, _cairo);
        (fallWindows[0].EndUtc - fallWindows[0].StartUtc).Should().Be(TimeSpan.FromHours(expectedFallHours));
    }

    [Fact]
    public async Task ScheduleImpact_WhenUpcomingBookingsExist_RequiresConfirmationAsync()
    {
        await using var db = CreateVenuesDb();
        var venue = Venue.Create(_venueId, "Club", "club", null, _ownerId, "Cairo", "Cairo", "Maadi", null, null, "+201000000000", null, null, _now);
        var pitch = Pitch.Create(_pitchId, _venueId, "Main", SurfaceType.ArtificialTurfValue, 5, 40m, 20m, null, _now);
        db.Venues.Add(venue);
        db.Pitches.Add(pitch);
        // Current schedule: Thursday 16:00 to 23:00
        db.OperatingSchedules.Add(OperatingSchedule.Create(Guid.NewGuid(), _venueId, DayOfWeek.Thursday, new TimeOnly(16, 0), new TimeOnly(23, 0), 60, _now));
        await db.SaveChangesAsync();

        // There is an upcoming confirmed booking on next Thursday at 21:00-22:00
        var nextThursday = new DateOnly(2026, 11, 12);
        var bookingStartUtc = TimeZoneInfo.ConvertTimeToUtc(nextThursday.ToDateTime(new TimeOnly(21, 0)), _cairo);
        var bookingEndUtc = bookingStartUtc.AddHours(1);
        var activeBooking = new UpcomingBookingRef(Guid.NewGuid(), "MLB-999", _pitchId, bookingStartUtc, bookingEndUtc, "Confirmed");

        var bookingsReader = new FakeBookingsReader([activeBooking]);
        var accessService = new FakeVenueAccess(VenueAccess.Owner(_venueId));
        var handlers = new SpecialScheduleHandlers(db, accessService, bookingsReader, _clock);

        // Propose changing Thursday hours to close early at 20:00 (which leaves 21:00 booking outside schedule!)
        // Step 1: Without confirm (Confirm = false)
        var unconfirmed = await handlers.HandleAsync(new SetPitchWeeklyHoursCommand(
            _venueId, _ownerId, _pitchId,
            [new WeeklyHoursInput(DayOfWeek.Thursday, new TimeOnly(16, 0), new TimeOnly(20, 0), false)],
            Confirm: false));

        unconfirmed.IsSuccess.Should().BeTrue();
        unconfirmed.Value.Applied.Should().BeFalse("Schedule change must NOT be applied without explicit confirmation when bookings are affected");
        unconfirmed.Value.AffectedBookings.Should().ContainSingle().Which.Reference.Should().Be("MLB-999");

        // Verify DB was not modified
        var existingHours = await db.PitchWeeklyHours.Where(h => h.PitchId == _pitchId).ToListAsync();
        existingHours.Should().BeEmpty();

        // Step 2: With confirm (Confirm = true)
        var confirmed = await handlers.HandleAsync(new SetPitchWeeklyHoursCommand(
            _venueId, _ownerId, _pitchId,
            [new WeeklyHoursInput(DayOfWeek.Thursday, new TimeOnly(16, 0), new TimeOnly(20, 0), false)],
            Confirm: true));

        confirmed.IsSuccess.Should().BeTrue();
        confirmed.Value.Applied.Should().BeTrue("Schedule change must be applied when caller confirms");

        // Verify DB was modified
        var savedHours = await db.PitchWeeklyHours.SingleAsync(h => h.PitchId == _pitchId);
        savedHours.CloseTime.Should().Be(new TimeOnly(20, 0));
    }

    [Fact]
    public async Task BlockedSlots_RejectsBookingConflicts_AndExistingBlockOverlapsAsync()
    {
        await using var db = CreateVenuesDb();
        var venue = Venue.Create(_venueId, "Club", "club", null, _ownerId, "Cairo", "Cairo", "Maadi", null, null, "+201000000000", null, null, _now);
        var pitch = Pitch.Create(_pitchId, _venueId, "Main", SurfaceType.ArtificialTurfValue, 5, 40m, 20m, null, _now);
        db.Venues.Add(venue);
        db.Pitches.Add(pitch);
        await db.SaveChangesAsync();

        var venueRepo = new VenueRepository(db);
        var pitchRepo = new PitchRepository(db);
        var blockedRepo = new BlockedSlotRepository(db);
        var auditLogger = new NoopVenueAudit();

        var slotStart = _now.AddDays(1);
        var slotEnd = slotStart.AddHours(2);

        // Case 1: Conflicting booking exists in window -> returns BlockedSlot.ConflictingBookings
        var conflictChecker = new FakeConflictChecker(hasConflict: true);
        var handler1 = new CreateBlockedSlotHandler(venueRepo, pitchRepo, blockedRepo, conflictChecker, auditLogger, db, _clock, NullLogger<CreateBlockedSlotHandler>.Instance);

        var confResult = await handler1.HandleAsync(new CreateBlockedSlotCommand(_venueId, _pitchId, _ownerId, slotStart, slotEnd, "Maintenance"));
        confResult.IsFailure.Should().BeTrue();
        confResult.Error.Code.Should().Be("BlockedSlot.ConflictingBookings");

        // Case 2: No booking conflict -> block created successfully
        var cleanChecker = new FakeConflictChecker(hasConflict: false);
        var handler2 = new CreateBlockedSlotHandler(venueRepo, pitchRepo, blockedRepo, cleanChecker, auditLogger, db, _clock, NullLogger<CreateBlockedSlotHandler>.Instance);

        var successResult = await handler2.HandleAsync(new CreateBlockedSlotCommand(_venueId, _pitchId, _ownerId, slotStart, slotEnd, "Pitch resurfacing"));
        successResult.IsSuccess.Should().BeTrue();
        var blockId = successResult.Value.Id;

        // Case 3: Overlapping block for same pitch -> returns BlockedSlot.Overlap
        var overlapResult = await handler2.HandleAsync(new CreateBlockedSlotCommand(_venueId, _pitchId, _ownerId, slotStart.AddHours(1), slotEnd.AddHours(1), "Overlapping block"));
        overlapResult.IsFailure.Should().BeTrue();
        overlapResult.Error.Code.Should().Be("BlockedSlot.Overlap");

        // Case 4: Adjacent block (starts exactly when first block ends) -> Allowed
        var adjacentResult = await handler2.HandleAsync(new CreateBlockedSlotCommand(_venueId, _pitchId, _ownerId, slotEnd, slotEnd.AddHours(1), "Adjacent cleaning"));
        adjacentResult.IsSuccess.Should().BeTrue();

        // Case 5: Delete blocked slot under wrong venue -> Returns BlockedSlot.NotFound
        var deleteHandler = new DeleteBlockedSlotHandler(blockedRepo, venueRepo, auditLogger, db, _clock, NullLogger<DeleteBlockedSlotHandler>.Instance);
        var wrongVenueDelete = await deleteHandler.HandleAsync(new DeleteBlockedSlotCommand(Guid.NewGuid(), blockId, _ownerId));
        wrongVenueDelete.IsFailure.Should().BeTrue();
        wrongVenueDelete.Error.Code.Should().Be("BlockedSlot.NotFound");

        // Case 6: Delete blocked slot successfully
        var validDelete = await deleteHandler.HandleAsync(new DeleteBlockedSlotCommand(_venueId, blockId, _ownerId));
        validDelete.IsSuccess.Should().BeTrue();
    }

    private sealed class FakeBookingsReader(IReadOnlyList<UpcomingBookingRef> bookings) : IUpcomingBookingsReader
    {
        public Task<IReadOnlyList<UpcomingBookingRef>> GetActiveAsync(
            IReadOnlyCollection<Guid> pitchIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UpcomingBookingRef>>(bookings.Where(b => pitchIds.Contains(b.PitchId) && b.EndUtc > fromUtc && b.StartUtc < toUtc).ToList());
    }

    private sealed class FakeVenueAccess(VenueAccess access) : IVenueAccessService
    {
        public Task<VenueAccess> GetAccessAsync(Guid userId, Guid venueId, CancellationToken cancellationToken = default) => Task.FromResult(access);
        public Task<IReadOnlyList<VenueAccess>> GetAccessibleVenuesAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<VenueAccess>>([access]);
    }

    private sealed class FakeConflictChecker(bool hasConflict) : IBookingConflictChecker
    {
        public Task<bool> HasActiveBookingsAsync(Guid pitchId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(hasConflict);
    }

    private sealed class NoopVenueAudit : IVenueAuditLogger
    {
        public Task LogAsync(string action, string entityName, Guid entityId, Guid userId, object? metadata = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(_now, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
