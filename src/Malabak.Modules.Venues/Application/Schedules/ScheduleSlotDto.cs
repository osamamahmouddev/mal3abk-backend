namespace Malabak.Modules.Venues.Application.Schedules;

public sealed record ScheduleSlotDto(
    DateOnly BusinessDate,
    int SlotIndex,
    DateTime StartTimeCairo,
    DateTime EndTimeCairo,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    bool IsOvernight);
