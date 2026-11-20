using Malabak.Modules.Venues.Application.Venues;
using Malabak.Modules.Venues.Domain;

namespace Malabak.Modules.Venues.Application.Common;

public static class VenueMapper
{
    public static VenueDto ToDto(Venue venue)
    {
        return new VenueDto(
            venue.Id,
            venue.Name,
            venue.Slug,
            venue.Description,
            venue.OwnerId,
            venue.Address,
            venue.City,
            venue.Area,
            venue.Latitude,
            venue.Longitude,
            venue.PhoneNumber,
            venue.ApprovalStatus.ToString(),
            venue.IsApproved,
            venue.IsPublished,
            venue.Facilities.ToList(),
            venue.Photos.ToList(),
            venue.Pitches.Select(PitchMapper.ToDto).ToList(),
            venue.PricingModel.ToString(),
            venue.CustomCommissionEnabled ? venue.CommissionType.ToString() : null,
            venue.CustomCommissionEnabled ? venue.CommissionRate : null);
    }
}
