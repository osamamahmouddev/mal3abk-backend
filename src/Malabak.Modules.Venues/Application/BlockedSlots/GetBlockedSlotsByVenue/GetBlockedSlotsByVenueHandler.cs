using Malabak.Modules.Venues.Application.BlockedSlots;
using Malabak.Modules.Venues.Application.Common;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;

namespace Malabak.Modules.Venues.Application.BlockedSlots.GetBlockedSlotsByVenue;

public sealed class GetBlockedSlotsByVenueHandler : IQueryHandler<GetBlockedSlotsByVenueQuery, Result<IReadOnlyList<BlockedSlotDto>>>
{
    private readonly IVenueAccessService _accessService;
    private readonly IBlockedSlotRepository _blockedSlotRepository;

    public GetBlockedSlotsByVenueHandler(IVenueAccessService accessService, IBlockedSlotRepository blockedSlotRepository)
    {
        _accessService = accessService;
        _blockedSlotRepository = blockedSlotRepository;
    }

    public async Task<Result<IReadOnlyList<BlockedSlotDto>>> HandleAsync(
        GetBlockedSlotsByVenueQuery query,
        CancellationToken cancellationToken = default)
    {
        var access = await _accessService.GetAccessAsync(query.UserId, query.VenueId, cancellationToken);
        if (!access.Can(Permissions.VenueView))
        {
            return Result.Failure<IReadOnlyList<BlockedSlotDto>>(VenueAccessErrors.VenueNotFound);
        }

        var slots = await _blockedSlotRepository.GetByVenueIdAsync(query.VenueId, cancellationToken);
        IReadOnlyList<BlockedSlotDto> dtos = slots.Select(BlockedSlotMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}
