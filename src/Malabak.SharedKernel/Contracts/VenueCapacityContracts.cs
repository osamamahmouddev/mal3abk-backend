namespace Malabak.SharedKernel.Contracts;

/// <summary>Venues-module contract for dashboards and reports: how many pitch-minutes a venue is open, per business date.</summary>
public interface IVenueCapacityReader
{
    /// <summary>
    /// Open minutes per Cairo business date in [<paramref name="from"/>, <paramref name="to"/>], summed over the venue's active
    /// pitches (or one pitch). Opening hours, special days and closures are applied; blocked slots are not subtracted.
    /// </summary>
    Task<IReadOnlyDictionary<DateOnly, int>> GetOpenMinutesAsync(
        Guid venueId, DateOnly from, DateOnly to, Guid? pitchId = null, CancellationToken cancellationToken = default);

    /// <summary>Total open minutes in the range for each active pitch of the venue, with the pitch name (utilization reports).</summary>
    Task<IReadOnlyList<PitchOpenMinutes>> GetOpenMinutesByPitchAsync(
        Guid venueId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

public sealed record PitchOpenMinutes(Guid PitchId, string Name, int OpenMinutes);
