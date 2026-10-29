using Malabak.Modules.Venues.Application.Schedules;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Schedules.GetVenueSlots;

public sealed record GetVenueSlotsQuery(Guid VenueId, DateOnly Date, Guid UserId) : IQuery<Result<IReadOnlyList<ScheduleSlotDto>>>;
