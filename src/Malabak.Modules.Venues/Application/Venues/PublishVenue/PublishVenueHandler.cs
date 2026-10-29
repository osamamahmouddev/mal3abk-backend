using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Venues.Application.Venues.PublishVenue;

public sealed class PublishVenueHandler : ICommandHandler<PublishVenueCommand, Result>
{
    private readonly IVenueRepository _venueRepository;
    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<PublishVenueHandler> _logger;

    public PublishVenueHandler(
        IVenueRepository venueRepository,
        VenuesDbContext dbContext,
        IClock clock,
        ILogger<PublishVenueHandler> logger)
    {
        _venueRepository = venueRepository;
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(
        PublishVenueCommand command,
        CancellationToken cancellationToken = default)
    {
        var venue = await _venueRepository.GetByIdAsync(command.VenueId, cancellationToken);
        if (venue is null)
        {
            return Result.Failure(new Error("Venue.NotFound", "Venue not found."));
        }

        if (venue.OwnerId != command.OwnerId)
        {
            return Result.Failure(new Error("Venue.Forbidden", "You are not authorized to modify this venue."));
        }

        try
        {
            venue.SetPublished(command.IsPublished, _clock.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(new Error("Venue.NotApproved", ex.Message));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Venue {VenueId} publication set to {IsPublished} by owner {OwnerId}", venue.Id, command.IsPublished, command.OwnerId);

        return Result.Success();
    }
}
