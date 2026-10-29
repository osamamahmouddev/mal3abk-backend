using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Venues.Application.Venues.DeleteVenue;

public sealed class DeleteVenueHandler : ICommandHandler<DeleteVenueCommand, Result>
{
    private readonly IVenueRepository _venueRepository;
    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<DeleteVenueHandler> _logger;

    public DeleteVenueHandler(
        IVenueRepository venueRepository,
        VenuesDbContext dbContext,
        IClock clock,
        ILogger<DeleteVenueHandler> logger)
    {
        _venueRepository = venueRepository;
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(
        DeleteVenueCommand command,
        CancellationToken cancellationToken = default)
    {
        var venue = await _venueRepository.GetByIdAsync(command.VenueId, cancellationToken);
        if (venue is null)
        {
            return Result.Failure(new Error("Venue.NotFound", "Venue not found."));
        }

        if (venue.OwnerId != command.OwnerId)
        {
            return Result.Failure(new Error("Venue.Forbidden", "You are not authorized to delete this venue."));
        }

        _venueRepository.Delete(venue, _clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Venue {VenueId} deleted by owner {OwnerId}", venue.Id, command.OwnerId);

        return Result.Success();
    }
}
