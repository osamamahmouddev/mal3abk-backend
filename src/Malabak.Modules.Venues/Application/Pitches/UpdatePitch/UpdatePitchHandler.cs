using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Pitches;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Venues.Application.Pitches.UpdatePitch;

public sealed class UpdatePitchHandler : ICommandHandler<UpdatePitchCommand, Result<PitchDto>>
{
    private readonly IPitchRepository _pitchRepository;
    private readonly IVenueRepository _venueRepository;
    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<UpdatePitchHandler> _logger;

    public UpdatePitchHandler(
        IPitchRepository pitchRepository,
        IVenueRepository venueRepository,
        VenuesDbContext dbContext,
        IClock clock,
        ILogger<UpdatePitchHandler> logger)
    {
        _pitchRepository = pitchRepository;
        _venueRepository = venueRepository;
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<PitchDto>> HandleAsync(
        UpdatePitchCommand command,
        CancellationToken cancellationToken = default)
    {
        var pitch = await _pitchRepository.GetByIdAsync(command.PitchId, cancellationToken);
        if (pitch is null)
        {
            return Result.Failure<PitchDto>(new Error("Pitch.NotFound", "Pitch not found."));
        }

        var venue = await _venueRepository.GetByIdAsync(pitch.VenueId, cancellationToken);
        if (venue is null || venue.OwnerId != command.OwnerId)
        {
            return Result.Failure<PitchDto>(new Error("Pitch.Forbidden", "You are not authorized to update this pitch."));
        }

        pitch.Update(
            command.Name,
            command.TurfType,
            command.Capacity,
            command.LengthMeters,
            command.WidthMeters,
            command.Photos,
            _clock.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Pitch {PitchId} ('{PitchName}') updated successfully", pitch.Id, pitch.Name);

        return Result.Success(PitchMapper.ToDto(pitch));
    }
}
