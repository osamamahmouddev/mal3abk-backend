using Malabak.Modules.Venues.Application.Schedules;
using Malabak.Modules.Venues.Domain;

namespace Malabak.Modules.Venues.Application.Common;

public static class ScheduleMapper
{
    public static OperatingScheduleDto ToDto(OperatingSchedule schedule)
    {
        return new OperatingScheduleDto(
            schedule.Id,
            schedule.VenueId,
            schedule.DayOfWeek,
            schedule.OpenTime,
            schedule.CloseTime,
            schedule.SlotDurationMinutes,
            schedule.IsActive,
            schedule.IsOvernight,
            schedule.TotalDurationMinutes);
    }

    public static ScheduleSlotDto ToDto(ScheduleSlot slot)
    {
        return new ScheduleSlotDto(
            slot.BusinessDate,
            slot.SlotIndex,
            slot.StartTimeCairo,
            slot.EndTimeCairo,
            slot.StartTimeUtc,
            slot.EndTimeUtc,
            slot.IsOvernight);
    }
}
