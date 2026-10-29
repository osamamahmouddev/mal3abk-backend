using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Venues.PublishVenue;

public sealed record PublishVenueCommand(Guid VenueId, Guid OwnerId, bool IsPublished) : ICommand<Result>;
