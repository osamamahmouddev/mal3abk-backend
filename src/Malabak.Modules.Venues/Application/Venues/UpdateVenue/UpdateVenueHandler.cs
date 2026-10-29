using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Venues;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Venues.Application.Venues.UpdateVenue;

public sealed class UpdateVenueHandler : ICommandHandler<UpdateVenueCommand, Result<VenueDto>>
{
    private readonly IVenueRepository _venueRepository;
    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<UpdateVenueHandler> _logger;

    public UpdateVenueHandler(
        IVenueRepository venueRepository,
        VenuesDbContext dbContext,
        IClock clock,
        ILogger<UpdateVenueHandler> logger)
    {
        _venueRepository = venueRepository;
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<VenueDto>> HandleAsync(
        UpdateVenueCommand command,
        CancellationToken cancellationToken = default)
    {
        var venue = await _venueRepository.GetByIdWithPitchesAsync(command.VenueId, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<VenueDto>(new Error(
                "Venue.NotFound",
                "Venue not found."));
        }

        // Tenant authorization check
        if (!await VenuePermissionCheck.CanAsync(_dbContext, command.OwnerId, venue.Id, Permissions.VenueEdit, cancellationToken))
        {
            return Result.Failure<VenueDto>(new Error(
                "Venue.Forbidden",
                "You are not authorized to update this venue."));
        }

        venue.UpdateDetails(
            command.Name,
            command.Description,
            command.Address,
            command.City,
            command.Area,
            command.Latitude,
            command.Longitude,
            command.PhoneNumber,
            command.Facilities,
            command.Photos,
            _clock.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Venue {VenueId} updated by owner {OwnerId}", venue.Id, command.OwnerId);

        return Result.Success(VenueMapper.ToDto(venue));
    }
}
