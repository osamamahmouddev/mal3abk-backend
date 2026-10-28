using Malabak.Modules.Venues.Application.BlockedSlots;
using Malabak.Modules.Venues.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.BlockedSlots.GetBlockedSlotsByVenue;

public sealed class GetBlockedSlotsByVenueHandler : IQueryHandler<GetBlockedSlotsByVenueQuery, Result<IReadOnlyList<BlockedSlotDto>>>
{
    private readonly IVenueRepository _venueRepository;
    private readonly IBlockedSlotRepository _blockedSlotRepository;

    public GetBlockedSlotsByVenueHandler(IVenueRepository venueRepository, IBlockedSlotRepository blockedSlotRepository)
    {
        _venueRepository = venueRepository;
        _blockedSlotRepository = blockedSlotRepository;
    }

    public async Task<Result<IReadOnlyList<BlockedSlotDto>>> HandleAsync(
        GetBlockedSlotsByVenueQuery query,
        CancellationToken cancellationToken = default)
    {
        var venue = await _venueRepository.GetByIdAsync(query.VenueId, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<IReadOnlyList<BlockedSlotDto>>(new Error("Venue.NotFound", "Venue not found."));
        }

        if (venue.OwnerId != query.UserId)
        {
            return Result.Failure<IReadOnlyList<BlockedSlotDto>>(new Error("Venue.Forbidden", "You are not authorized to view blocked slots for this venue."));
        }

        var slots = await _blockedSlotRepository.GetByVenueIdAsync(query.VenueId, cancellationToken);
        IReadOnlyList<BlockedSlotDto> dtos = slots.Select(BlockedSlotMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}
