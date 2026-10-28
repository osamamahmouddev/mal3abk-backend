namespace Malabak.Modules.Venues.Application.Pitches;

public sealed record PitchDto(
    Guid Id,
    Guid VenueId,
    string Name,
    string TurfType,
    string SurfaceDisplayName,
    string SurfaceArabicDisplayName,
    int Capacity,
    string Format,
    decimal? LengthMeters,
    decimal? WidthMeters,
    bool IsActive,
    IReadOnlyList<string> Photos);
