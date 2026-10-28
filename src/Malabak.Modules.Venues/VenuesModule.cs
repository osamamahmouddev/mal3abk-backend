using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Pitches;
using Malabak.Modules.Venues.Application.Pitches.CreatePitch;
using Malabak.Modules.Venues.Application.Pitches.DeletePitch;
using Malabak.Modules.Venues.Application.Pitches.GetPitchById;
using Malabak.Modules.Venues.Application.Pitches.GetPitchesByVenue;
using Malabak.Modules.Venues.Application.Pitches.UpdatePitch;
using Malabak.Modules.Venues.Application.Schedules;
using Malabak.Modules.Venues.Application.Schedules.GetVenueSchedules;
using Malabak.Modules.Venues.Application.Schedules.GetVenueSlots;
using Malabak.Modules.Venues.Application.Schedules.UpdateVenueSchedules;
using Malabak.Modules.Venues.Application.Venues;
using Malabak.Modules.Venues.Application.Venues.CreateVenue;
using Malabak.Modules.Venues.Application.Venues.DeleteVenue;
using Malabak.Modules.Venues.Application.Venues.GetVenueById;
using Malabak.Modules.Venues.Application.Venues.GetVenuesByOwner;
using Malabak.Modules.Venues.Application.Venues.PublishVenue;
using Malabak.Modules.Venues.Application.Venues.UpdateVenue;
using Malabak.Modules.Venues.Infrastructure.Repositories;
using Malabak.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Modules.Venues;

public static class VenuesModule
{
    public const string Schema = "venues";

    public static IServiceCollection AddVenuesModule(this IServiceCollection services)
    {
        // Repositories
        services.AddScoped<IVenueRepository, VenueRepository>();
        services.AddScoped<IPitchRepository, PitchRepository>();

        // Venue Handlers
        services.AddScoped<ICommandHandler<CreateVenueCommand, Result<VenueDto>>, CreateVenueHandler>();
        services.AddScoped<ICommandHandler<UpdateVenueCommand, Result<VenueDto>>, UpdateVenueHandler>();
        services.AddScoped<ICommandHandler<DeleteVenueCommand, Result>, DeleteVenueHandler>();
        services.AddScoped<ICommandHandler<PublishVenueCommand, Result>, PublishVenueHandler>();
        services.AddScoped<IQueryHandler<GetVenueByIdQuery, Result<VenueDto>>, GetVenueByIdHandler>();
        services.AddScoped<IQueryHandler<GetVenuesByOwnerQuery, Result<IReadOnlyList<VenueDto>>>, GetVenuesByOwnerHandler>();

        // Pitch Handlers
        services.AddScoped<ICommandHandler<CreatePitchCommand, Result<PitchDto>>, CreatePitchHandler>();
        services.AddScoped<ICommandHandler<UpdatePitchCommand, Result<PitchDto>>, UpdatePitchHandler>();
        services.AddScoped<ICommandHandler<DeletePitchCommand, Result>, DeletePitchHandler>();
        services.AddScoped<IQueryHandler<GetPitchByIdQuery, Result<PitchDto>>, GetPitchByIdHandler>();
        services.AddScoped<IQueryHandler<GetPitchesByVenueQuery, Result<IReadOnlyList<PitchDto>>>, GetPitchesByVenueHandler>();

        // Operating Schedule Handlers
        services.AddScoped<ICommandHandler<UpdateVenueSchedulesCommand, Result<IReadOnlyList<OperatingScheduleDto>>>, UpdateVenueSchedulesHandler>();
        services.AddScoped<IQueryHandler<GetVenueSchedulesQuery, Result<IReadOnlyList<OperatingScheduleDto>>>, GetVenueSchedulesHandler>();
        services.AddScoped<IQueryHandler<GetVenueSlotsQuery, Result<IReadOnlyList<ScheduleSlotDto>>>, GetVenueSlotsHandler>();

        return services;
    }
}
