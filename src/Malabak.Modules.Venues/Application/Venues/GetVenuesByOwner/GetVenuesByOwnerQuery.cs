using Malabak.Modules.Venues.Application.Venues;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Venues.GetVenuesByOwner;

public sealed record GetVenuesByOwnerQuery(Guid OwnerId) : IQuery<Result<IReadOnlyList<VenueDto>>>;
