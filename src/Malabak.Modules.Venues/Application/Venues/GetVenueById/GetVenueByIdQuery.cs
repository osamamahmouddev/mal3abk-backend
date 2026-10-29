using Malabak.Modules.Venues.Application.Venues;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Venues.GetVenueById;

public sealed record GetVenueByIdQuery(Guid VenueId) : IQuery<Result<VenueDto>>;
