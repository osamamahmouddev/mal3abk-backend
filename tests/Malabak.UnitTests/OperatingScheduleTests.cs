using FluentAssertions;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Schedules.GetVenueSchedules;
using Malabak.Modules.Venues.Application.Schedules.GetVenueSlots;
using Malabak.Modules.Venues.Application.Schedules.UpdateVenueSchedules;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.Modules.Venues.Infrastructure.Repositories;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class OperatingScheduleTests
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
    public void OperatingSchedule_Create_ShouldInitializePropertiesCorrectly()
    {
        var id = Guid.NewGuid();
        var venueId = Guid.NewGuid();
        var open = new TimeOnly(10, 0);
        var close = new TimeOnly(2, 0); // 02:00 AM next day (overnight)

        var schedule = OperatingSchedule.Create(
            id,
            venueId,
            DayOfWeek.Thursday,
            open,
            close,
            60,
            _clock.UtcNow);

        schedule.Id.Should().Be(id);
        schedule.VenueId.Should().Be(venueId);
        schedule.DayOfWeek.Should().Be(DayOfWeek.Thursday);
        schedule.OpenTime.Should().Be(open);
        schedule.CloseTime.Should().Be(close);
        schedule.SlotDurationMinutes.Should().Be(60);
        schedule.IsActive.Should().BeTrue();
        schedule.IsOvernight.Should().BeTrue();
        schedule.TotalDurationMinutes.Should().Be(960); // 16 hours * 60 = 960 minutes
    }

    [Fact]
    public void OperatingSchedule_Create_ThrowsWhenSlotDurationNonPositive()
    {
        var act = () => OperatingSchedule.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DayOfWeek.Sunday,
            new TimeOnly(9, 0),
            new TimeOnly(18, 0),
            0,
            _clock.UtcNow);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Slot duration must be positive*");
    }

    [Fact]
    public void OvernightSpan_Calculation_PreservesBusinessDate_AndFlagsOvernightSlots()
    {
        // Sat-Thu 10:00 AM to 02:00 AM next day (16 hours = 16 1-hour slots)
        var schedule = OperatingSchedule.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DayOfWeek.Saturday,
            new TimeOnly(10, 0),
            new TimeOnly(2, 0),
            60,
            _clock.UtcNow);

        var businessDate = new DateOnly(2026, 5, 9); // Saturday
        var slots = schedule.GenerateSlots(businessDate, _clock.CairoTimeZone);

        slots.Should().HaveCount(16);

        // First slot: 10:00 AM to 11:00 AM on businessDate
        var firstSlot = slots[0];
        firstSlot.SlotIndex.Should().Be(0);
        firstSlot.BusinessDate.Should().Be(businessDate);
        firstSlot.StartTimeCairo.Should().Be(new DateTime(2026, 5, 9, 10, 0, 0, DateTimeKind.Unspecified));
        firstSlot.EndTimeCairo.Should().Be(new DateTime(2026, 5, 9, 11, 0, 0, DateTimeKind.Unspecified));
        firstSlot.IsOvernight.Should().BeFalse();

        // Daytime slots before midnight: IsOvernight = false
        // Slot 13: 23:00 to 00:00
        var slot23 = slots[13];
        slot23.SlotIndex.Should().Be(13);
        slot23.BusinessDate.Should().Be(businessDate);
        slot23.StartTimeCairo.Should().Be(new DateTime(2026, 5, 9, 23, 0, 0, DateTimeKind.Unspecified));
        slot23.EndTimeCairo.Should().Be(new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Unspecified));
        slot23.IsOvernight.Should().BeFalse();

        // Overnight slots after midnight: IsOvernight = true, BusinessDate remains Saturday
        // Slot 14: 00:00 to 01:00 (Sunday morning)
        var slot00 = slots[14];
        slot00.SlotIndex.Should().Be(14);
        slot00.BusinessDate.Should().Be(businessDate);
        slot00.StartTimeCairo.Should().Be(new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Unspecified));
        slot00.EndTimeCairo.Should().Be(new DateTime(2026, 5, 10, 1, 0, 0, DateTimeKind.Unspecified));
        slot00.IsOvernight.Should().BeTrue();

        // Slot 15: 01:00 to 02:00 (Sunday morning)
        var lastSlot = slots[15];
        lastSlot.SlotIndex.Should().Be(15);
        lastSlot.BusinessDate.Should().Be(businessDate);
        lastSlot.StartTimeCairo.Should().Be(new DateTime(2026, 5, 10, 1, 0, 0, DateTimeKind.Unspecified));
        lastSlot.EndTimeCairo.Should().Be(new DateTime(2026, 5, 10, 2, 0, 0, DateTimeKind.Unspecified));
        lastSlot.IsOvernight.Should().BeTrue();
    }

    [Fact]
    public void RegularDaytime_Schedule_Calculation_GeneratesCorrectSlots()
    {
        // 10:00 AM to 22:00 PM same day (12 hours = 12 1-hour slots)
        var schedule = OperatingSchedule.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DayOfWeek.Monday,
            new TimeOnly(10, 0),
            new TimeOnly(22, 0),
            60,
            _clock.UtcNow);

        var businessDate = new DateOnly(2026, 5, 11);
        var slots = schedule.GenerateSlots(businessDate, _clock.CairoTimeZone);

        schedule.IsOvernight.Should().BeFalse();
        schedule.TotalDurationMinutes.Should().Be(720);
        slots.Should().HaveCount(12);
        slots.All(s => !s.IsOvernight).Should().BeTrue();
        slots.All(s => s.BusinessDate == businessDate).Should().BeTrue();
    }

    [Fact]
    public void AfricaCairo_Timezone_WinterAndSummerOffsets_ConvertCorrectly()
    {
        var cairoTz = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        // Winter (January): UTC+2
        var winterLocal = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified);
        var winterUtc = OperatingSchedule.ConvertToUtc(winterLocal, cairoTz);
        winterUtc.Should().Be(new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc));

        // Summer (July): UTC+3 (Daylight Saving Time in Egypt)
        var summerLocal = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Unspecified);
        var summerUtc = OperatingSchedule.ConvertToUtc(summerLocal, cairoTz);
        summerUtc.Should().Be(new DateTime(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void AfricaCairo_DST_SpringForwardTransition_HandlesSkippedHour()
    {
        var cairoTz = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        // In 2026, Egypt DST transition starts on the last Friday of April (April 24, 2026 at midnight 00:00 -> 01:00)
        // If an operating slot falls on 00:30 on transition night:
        var transitionLocal = new DateTime(2026, 4, 24, 0, 30, 0, DateTimeKind.Unspecified);
        var utc = OperatingSchedule.ConvertToUtc(transitionLocal, cairoTz);

        // Convert back to Cairo time
        var localBack = TimeZoneInfo.ConvertTimeFromUtc(utc, cairoTz);
        localBack.Should().BeAfter(transitionLocal.AddMinutes(-1));
    }

    [Fact]
    public async Task UpdateVenueSchedulesHandler_UpdatesSchedules_WhenOwnerAsync()
    {
        using var dbContext = CreateDbContext();
        var venueRepo = new VenueRepository(dbContext);

        var ownerId = Guid.NewGuid();
        var venue = Venue.Create(
            Guid.NewGuid(), "Cairo Stadium Arena", "cairo-stadium", null,
            ownerId, "Nasr City", "Cairo", "Nasr City",
            null, null, "+201011119999", null, null, _clock.UtcNow);

        venueRepo.Add(venue);
        await dbContext.SaveChangesAsync();

        var handler = new UpdateVenueSchedulesHandler(venueRepo, dbContext, _clock, NullLogger<UpdateVenueSchedulesHandler>.Instance);

        var command = new UpdateVenueSchedulesCommand(
            venue.Id,
            ownerId,
            [
                new ScheduleItemInput(DayOfWeek.Thursday, new TimeOnly(16, 0), new TimeOnly(2, 0), 60, true),
                new ScheduleItemInput(DayOfWeek.Friday, new TimeOnly(14, 0), new TimeOnly(3, 0), 60, true)
            ]);

        var result = await handler.HandleAsync(command);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);

        var thursday = result.Value.First(s => s.DayOfWeek == DayOfWeek.Thursday);
        thursday.IsOvernight.Should().BeTrue();
        thursday.TotalDurationMinutes.Should().Be(600); // 16:00 to 02:00 = 10 hrs = 600 min

        var friday = result.Value.First(s => s.DayOfWeek == DayOfWeek.Friday);
        friday.IsOvernight.Should().BeTrue();
        friday.TotalDurationMinutes.Should().Be(780); // 14:00 to 03:00 = 13 hrs = 780 min
    }

    [Fact]
    public async Task UpdateVenueSchedulesHandler_Rejects_WhenNotOwnerAsync()
    {
        using var dbContext = CreateDbContext();
        var venueRepo = new VenueRepository(dbContext);

        var owner1 = Guid.NewGuid();
        var owner2 = Guid.NewGuid();
        var venue = Venue.Create(
            Guid.NewGuid(), "Zayed Club", "zayed-club", null,
            owner1, "Sheikh Zayed", "Giza", "Zayed",
            null, null, "+201011118888", null, null, _clock.UtcNow);

        venueRepo.Add(venue);
        await dbContext.SaveChangesAsync();

        var handler = new UpdateVenueSchedulesHandler(venueRepo, dbContext, _clock, NullLogger<UpdateVenueSchedulesHandler>.Instance);

        var command = new UpdateVenueSchedulesCommand(
            venue.Id,
            owner2, // Different owner
            [new ScheduleItemInput(DayOfWeek.Sunday, new TimeOnly(10, 0), new TimeOnly(22, 0), 60, true)]);

        var result = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Venue.Forbidden");
    }

    [Fact]
    public async Task GetVenueSlotsHandler_ReturnsSlots_ForSpecificBusinessDateAsync()
    {
        using var dbContext = CreateDbContext();
        var venueRepo = new VenueRepository(dbContext);

        var ownerId = Guid.NewGuid();
        var venue = Venue.Create(
            Guid.NewGuid(), "Gezira Club", "gezira-club", null,
            ownerId, "Zamalek", "Cairo", "Zamalek",
            null, null, "+201011117777", null, null, _clock.UtcNow);

        var schedule = OperatingSchedule.Create(
            Guid.NewGuid(),
            venue.Id,
            DayOfWeek.Friday,
            new TimeOnly(14, 0),
            new TimeOnly(2, 0), // 14:00 to 02:00 next day = 12 hours
            60,
            _clock.UtcNow);

        venue.SetOperatingSchedules([schedule], _clock.UtcNow);
        venueRepo.Add(venue);
        await dbContext.SaveChangesAsync();

        var slotsHandler = new GetVenueSlotsHandler(venueRepo, _clock);

        // Query Friday 2026-05-15
        var fridayDate = new DateOnly(2026, 5, 15);
        var slotsResult = await slotsHandler.HandleAsync(new GetVenueSlotsQuery(venue.Id, fridayDate));

        slotsResult.IsSuccess.Should().BeTrue();
        slotsResult.Value.Should().HaveCount(12);
        slotsResult.Value[0].StartTimeCairo.Hour.Should().Be(14);
        slotsResult.Value[^1].StartTimeCairo.Hour.Should().Be(1); // 01:00 to 02:00
        slotsResult.Value[^1].IsOvernight.Should().BeTrue();
        slotsResult.Value.All(s => s.BusinessDate == fridayDate).Should().BeTrue();

        // Query Saturday 2026-05-16 (no schedule configured -> empty)
        var saturdayDate = new DateOnly(2026, 5, 16);
        var saturdayResult = await slotsHandler.HandleAsync(new GetVenueSlotsQuery(venue.Id, saturdayDate));

        saturdayResult.IsSuccess.Should().BeTrue();
        saturdayResult.Value.Should().BeEmpty();
    }

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
