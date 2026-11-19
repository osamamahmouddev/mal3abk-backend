using Malabak.Modules.Venues.Application.Pitches;

namespace Malabak.Modules.Venues.Application.Venues;

public sealed record VenueDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    Guid OwnerId,
    string Address,
    string City,
    string Area,
    double? Latitude,
    double? Longitude,
    string PhoneNumber,
    string ApprovalStatus,
    bool IsApproved,
    bool IsPublished,
    IReadOnlyList<string> Facilities,
    IReadOnlyList<string> Photos,
    IReadOnlyList<PitchDto> Pitches,
    string PricingModel = "Subscription");
