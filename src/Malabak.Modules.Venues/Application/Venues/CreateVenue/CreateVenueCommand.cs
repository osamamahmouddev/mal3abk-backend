using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Venues.CreateVenue;

public sealed record CreateVenueCommand(
    Guid OwnerId,
    string Name,
    string? Description,
    string Address,
    string City,
    string Area,
    double? Latitude,
    double? Longitude,
    string PhoneNumber,
    IEnumerable<string>? Facilities,
    IEnumerable<string>? Photos) : ICommand<Result<VenueDto>>;
