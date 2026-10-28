using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Venues.Application.Pitches.DeletePitch;

public sealed class DeletePitchHandler : ICommandHandler<DeletePitchCommand, Result>
{
    private readonly IPitchRepository _pitchRepository;
    private readonly IVenueRepository _venueRepository;
    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<DeletePitchHandler> _logger;

    public DeletePitchHandler(
        IPitchRepository pitchRepository,
        IVenueRepository venueRepository,
        VenuesDbContext dbContext,
        IClock clock,
        ILogger<DeletePitchHandler> logger)
    {
        _pitchRepository = pitchRepository;
        _venueRepository = venueRepository;
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(
        DeletePitchCommand command,
        CancellationToken cancellationToken = default)
    {
        var pitch = await _pitchRepository.GetByIdAsync(command.PitchId, cancellationToken);
        if (pitch is null)
        {
            return Result.Failure(new Error("Pitch.NotFound", "Pitch not found."));
        }

        var venue = await _venueRepository.GetByIdAsync(pitch.VenueId, cancellationToken);
        if (venue is null || venue.OwnerId != command.OwnerId)
        {
            return Result.Failure(new Error("Pitch.Forbidden", "You are not authorized to delete this pitch."));
        }

        pitch.MarkDeleted(_clock.UtcNow);
        venue.RemovePitch(pitch.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Pitch {PitchId} deleted by owner {OwnerId}", pitch.Id, command.OwnerId);

        return Result.Success();
    }
}
