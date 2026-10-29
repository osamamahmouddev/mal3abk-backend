using Malabak.Modules.Venues.Application.Pitches;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Pitches.GetPitchesByVenue;

public sealed record GetPitchesByVenueQuery(Guid VenueId) : IQuery<Result<IReadOnlyList<PitchDto>>>;
