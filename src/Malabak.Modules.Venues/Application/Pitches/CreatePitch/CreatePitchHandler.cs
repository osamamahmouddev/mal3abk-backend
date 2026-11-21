using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Pitches;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.Extensions.Logging;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Pitches.CreatePitch;

public sealed class CreatePitchHandler : ICommandHandler<CreatePitchCommand, Result<PitchDto>>
{
    private readonly IVenueRepository _venueRepository;
    private readonly IPitchRepository _pitchRepository;
    private readonly VenuesDbContext _dbContext;
    private readonly IEntitlementService _entitlements;
    private readonly IClock _clock;
    private readonly ILogger<CreatePitchHandler> _logger;

    public CreatePitchHandler(
        IVenueRepository venueRepository,
        IPitchRepository pitchRepository,
        VenuesDbContext dbContext,
        IEntitlementService entitlements,
        IClock clock,
        ILogger<CreatePitchHandler> logger)
    {
        _venueRepository = venueRepository;
        _pitchRepository = pitchRepository;
        _dbContext = dbContext;
        _entitlements = entitlements;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<PitchDto>> HandleAsync(
        CreatePitchCommand command,
        CancellationToken cancellationToken = default)
    {
        var venue = await _venueRepository.GetByIdAsync(command.VenueId, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<PitchDto>(new Error("Venue.NotFound", "Venue not found."));
        }

        if (!await VenuePermissionCheck.CanAsync(_dbContext, command.OwnerId, venue.Id, Permissions.PitchesManage, cancellationToken))
        {
            return Result.Failure<PitchDto>(new Error("Venue.Forbidden", "You are not authorized to add pitches to this venue."));
        }

        // Pitches across all the owner's venues, checked against the entitlement policy (no plan limits under the commission model).
        var ownerVenues = _dbContext.Venues.Where(v => v.OwnerId == venue.OwnerId && !v.IsDeleted).Select(v => v.Id);
        var pitchCount = await _dbContext.Pitches.CountAsync(p => ownerVenues.Contains(p.VenueId) && !p.IsDeleted, cancellationToken);
        var allowed = await _entitlements.EnsureWithinLimitAsync(venue.OwnerId, EntitlementResource.Pitches, pitchCount, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<PitchDto>(allowed.Error);
        }

        var pitch = Pitch.Create(
            id: Guid.NewGuid(),
            venueId: command.VenueId,
            name: command.Name,
            turfType: command.TurfType,
            capacity: command.Capacity,
            lengthMeters: command.LengthMeters,
            widthMeters: command.WidthMeters,
            photos: command.Photos,
            createdAtUtc: _clock.UtcNow);

        _pitchRepository.Add(pitch);
        venue.AddPitch(pitch);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Pitch {PitchId} ('{PitchName}') created for venue {VenueId}", pitch.Id, pitch.Name, venue.Id);

        return Result.Success(PitchMapper.ToDto(pitch));
    }
}
