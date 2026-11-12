using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Venues.Application.Pitches.DeletePitch;

public sealed class DeletePitchHandler : ICommandHandler<DeletePitchCommand, Result>
{
    private readonly IPitchRepository _pitchRepository;
    private readonly IVenueRepository _venueRepository;
    private readonly VenuesDbContext _dbContext;
    private readonly IUpcomingBookingsReader _bookings;
    private readonly IClock _clock;
    private readonly ILogger<DeletePitchHandler> _logger;

    public DeletePitchHandler(
        IPitchRepository pitchRepository,
        IVenueRepository venueRepository,
        VenuesDbContext dbContext,
        IUpcomingBookingsReader bookings,
        IClock clock,
        ILogger<DeletePitchHandler> logger)
    {
        _pitchRepository = pitchRepository;
        _venueRepository = venueRepository;
        _dbContext = dbContext;
        _bookings = bookings;
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
        if (venue is null || !await VenuePermissionCheck.CanAsync(_dbContext, command.OwnerId, venue.Id, Permissions.PitchesManage, cancellationToken))
        {
            return Result.Failure(new Error("Pitch.Forbidden", "You are not authorized to delete this pitch."));
        }

        // A pitch with bookings that still hold their slot cannot go: those players would be left on a deleted pitch.
        // Holds that have already run out are free and do not count.
        var nowUtc = _clock.UtcNow;
        var upcoming = await _bookings.GetActiveAsync([pitch.Id], nowUtc, nowUtc.AddYears(10), cancellationToken);
        if (upcoming.Any(b => b.HoldExpiresAtUtc is not { } expiresAtUtc || expiresAtUtc > nowUtc))
        {
            return Result.Failure(Error.Conflict("Pitch.HasActiveBookings", "This pitch has upcoming bookings. Cancel or move them before deleting the pitch, or pause the pitch instead."));
        }

        pitch.MarkDeleted(_clock.UtcNow);
        venue.RemovePitch(pitch.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Pitch {PitchId} deleted by owner {OwnerId}", pitch.Id, command.OwnerId);

        return Result.Success();
    }
}
