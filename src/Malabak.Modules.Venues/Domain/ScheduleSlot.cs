namespace Malabak.Modules.Venues.Domain;

public sealed record ScheduleSlot(
    DateOnly BusinessDate,
    int SlotIndex,
    DateTime StartTimeCairo,
    DateTime EndTimeCairo,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    bool IsOvernight);
