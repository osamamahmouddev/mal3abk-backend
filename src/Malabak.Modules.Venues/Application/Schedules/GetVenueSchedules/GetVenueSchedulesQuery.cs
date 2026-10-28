using Malabak.Modules.Venues.Application.Schedules;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Schedules.GetVenueSchedules;

public sealed record GetVenueSchedulesQuery(Guid VenueId) : IQuery<Result<IReadOnlyList<OperatingScheduleDto>>>;
