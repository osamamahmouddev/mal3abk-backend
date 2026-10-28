using Malabak.Modules.Venues.Application.Pitches;
using Malabak.Modules.Venues.Domain;

namespace Malabak.Modules.Venues.Application.Common;

public static class PitchMapper
{
    public static PitchDto ToDto(Pitch pitch)
    {
        return new PitchDto(
            pitch.Id,
            pitch.VenueId,
            pitch.Name,
            pitch.TurfType,
            pitch.Surface.DisplayName,
            pitch.Surface.ArabicDisplayName,
            pitch.Capacity,
            pitch.CapacityValue.Format,
            pitch.LengthMeters,
            pitch.WidthMeters,
            pitch.IsActive,
            pitch.Photos.ToList());
    }
}
