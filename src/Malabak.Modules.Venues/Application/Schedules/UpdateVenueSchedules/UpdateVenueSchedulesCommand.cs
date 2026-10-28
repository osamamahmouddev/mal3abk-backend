using Malabak.Modules.Venues.Application.Schedules;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Schedules.UpdateVenueSchedules;

public sealed record ScheduleItemInput(
    DayOfWeek DayOfWeek,
    TimeOnly OpenTime,
    TimeOnly CloseTime,
    int SlotDurationMinutes,
    bool IsActive);

public sealed record UpdateVenueSchedulesCommand(
    Guid VenueId,
    Guid OwnerId,
    IReadOnlyList<ScheduleItemInput> Schedules) : ICommand<Result<IReadOnlyList<OperatingScheduleDto>>>;
