namespace Malabak.Modules.Venues.Application.Schedules;

public sealed record OperatingScheduleDto(
    Guid Id,
    Guid VenueId,
    DayOfWeek DayOfWeek,
    TimeOnly OpenTime,
    TimeOnly CloseTime,
    int SlotDurationMinutes,
    bool IsActive,
    bool IsOvernight,
    int TotalDurationMinutes);
