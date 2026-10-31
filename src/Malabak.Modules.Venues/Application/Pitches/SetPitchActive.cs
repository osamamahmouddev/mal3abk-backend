using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Pitches;

/// <summary>The pitch after the change, plus bookings it still has from now on (they are kept; staff decide what to do).</summary>
public sealed record PitchActivationResult(PitchDto Pitch, int UpcomingBookings);

/// <summary>
/// Pauses or resumes a pitch (TASK-505). A paused pitch disappears from search and availability at once; bookings already
/// made are not cancelled automatically, so the response says how many remain for the staff to handle.
/// </summary>
public sealed class SetPitchActive
{
    private readonly VenuesDbContext _dbContext;
    private readonly IUpcomingBookingsReader _bookings;
    private readonly IClock _clock;

    public SetPitchActive(VenuesDbContext dbContext, IUpcomingBookingsReader bookings, IClock clock)
    {
        _dbContext = dbContext;
        _bookings = bookings;
        _clock = clock;
    }

    public async Task<Result<PitchActivationResult>> HandleAsync(Guid pitchId, Guid userId, bool isActive, CancellationToken cancellationToken = default)
    {
        var pitch = await _dbContext.Pitches.FirstOrDefaultAsync(p => p.Id == pitchId && !p.IsDeleted, cancellationToken);
        if (pitch is null || !await VenuePermissionCheck.CanAsync(_dbContext, userId, pitch.VenueId, Permissions.PitchesManage, cancellationToken))
        {
            // A pitch of a venue the user cannot manage looks like one that does not exist.
            return Result.Failure<PitchActivationResult>(VenueAccessErrors.PitchNotFound);
        }

        var nowUtc = _clock.UtcNow;
        pitch.SetActive(isActive, nowUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var upcoming = await _bookings.GetActiveAsync([pitch.Id], nowUtc, nowUtc.AddYears(1), cancellationToken);
        return Result.Success(new PitchActivationResult(PitchMapper.ToDto(pitch), upcoming.Count));
    }
}
