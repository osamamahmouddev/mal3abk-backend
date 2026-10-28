using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Venues.DeleteVenue;

public sealed record DeleteVenueCommand(Guid VenueId, Guid OwnerId) : ICommand<Result>;
