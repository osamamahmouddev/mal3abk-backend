using Malabak.Modules.Venues.Application.Pitches;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Pitches.CreatePitch;

public sealed record CreatePitchCommand(
    Guid VenueId,
    Guid OwnerId,
    string Name,
    string TurfType,
    int Capacity,
    decimal? LengthMeters,
    decimal? WidthMeters,
    IEnumerable<string>? Photos) : ICommand<Result<PitchDto>>;
