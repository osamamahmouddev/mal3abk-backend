using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Common;

public static class VenueAccessErrors
{
    /// <summary>Returned both when the venue doesn't exist and when the caller has no access, so ids can't be probed.</summary>
    public static readonly Error VenueNotFound = new("Venue.NotFound", "Venue not found.");

    public static readonly Error PitchNotFound = new("Pitch.NotFound", "Pitch not found.");
}
