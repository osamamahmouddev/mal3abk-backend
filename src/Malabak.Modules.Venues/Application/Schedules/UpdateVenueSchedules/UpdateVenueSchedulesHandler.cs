using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Schedules;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Venues.Application.Schedules.UpdateVenueSchedules;

public sealed class UpdateVenueSchedulesHandler : ICommandHandler<UpdateVenueSchedulesCommand, Result<IReadOnlyList<OperatingScheduleDto>>>
{
    private readonly IVenueRepository _venueRepository;
    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<UpdateVenueSchedulesHandler> _logger;

    public UpdateVenueSchedulesHandler(
        IVenueRepository venueRepository,
        VenuesDbContext dbContext,
        IClock clock,
        ILogger<UpdateVenueSchedulesHandler> logger)
    {
        _venueRepository = venueRepository;
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<OperatingScheduleDto>>> HandleAsync(
        UpdateVenueSchedulesCommand command,
        CancellationToken cancellationToken = default)
    {
        var venue = await _venueRepository.GetByIdWithSchedulesAsync(command.VenueId, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<IReadOnlyList<OperatingScheduleDto>>(new Error("Venue.NotFound", "Venue not found."));
        }

        if (venue.OwnerId != command.OwnerId)
        {
            return Result.Failure<IReadOnlyList<OperatingScheduleDto>>(new Error("Venue.Forbidden", "You are not authorized to update schedules for this venue."));
        }

        var existingSchedules = venue.OperatingSchedules.ToDictionary(s => s.DayOfWeek);
        var updatedSchedules = new List<OperatingSchedule>();
        var nowUtc = _clock.UtcNow;

        foreach (var item in command.Schedules)
        {
            if (existingSchedules.TryGetValue(item.DayOfWeek, out var existing))
            {
                existing.Update(item.OpenTime, item.CloseTime, item.SlotDurationMinutes, item.IsActive, nowUtc);
                updatedSchedules.Add(existing);
            }
            else
            {
                var newSchedule = OperatingSchedule.Create(
                    id: Guid.NewGuid(),
                    venueId: venue.Id,
                    dayOfWeek: item.DayOfWeek,
                    openTime: item.OpenTime,
                    closeTime: item.CloseTime,
                    slotDurationMinutes: item.SlotDurationMinutes,
                    createdAtUtc: nowUtc);

                _dbContext.OperatingSchedules.Add(newSchedule);
                updatedSchedules.Add(newSchedule);
            }
        }

        venue.SetOperatingSchedules(updatedSchedules, nowUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated {Count} operating schedules for venue {VenueId}", updatedSchedules.Count, venue.Id);

        var dtos = updatedSchedules
            .OrderBy(s => (int)s.DayOfWeek)
            .Select(ScheduleMapper.ToDto)
            .ToList();

        return Result.Success<IReadOnlyList<OperatingScheduleDto>>(dtos);
    }
}
