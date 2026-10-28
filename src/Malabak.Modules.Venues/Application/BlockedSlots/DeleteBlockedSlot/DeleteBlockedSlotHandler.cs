using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Venues.Application.BlockedSlots.DeleteBlockedSlot;

public sealed class DeleteBlockedSlotHandler : ICommandHandler<DeleteBlockedSlotCommand, Result>
{
    private readonly IBlockedSlotRepository _blockedSlotRepository;
    private readonly IVenueRepository _venueRepository;
    private readonly IVenueAuditLogger _auditLogger;
    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<DeleteBlockedSlotHandler> _logger;

    public DeleteBlockedSlotHandler(
        IBlockedSlotRepository blockedSlotRepository,
        IVenueRepository venueRepository,
        IVenueAuditLogger auditLogger,
        VenuesDbContext dbContext,
        IClock clock,
        ILogger<DeleteBlockedSlotHandler> logger)
    {
        _blockedSlotRepository = blockedSlotRepository;
        _venueRepository = venueRepository;
        _auditLogger = auditLogger;
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(
        DeleteBlockedSlotCommand command,
        CancellationToken cancellationToken = default)
    {
        var slot = await _blockedSlotRepository.GetByIdAsync(command.BlockedSlotId, cancellationToken);

        // A slot addressed through another venue's route is reported as missing so ids can't be probed across venues.
        if (slot is null || slot.VenueId != command.VenueId)
        {
            return Result.Failure(new Error("BlockedSlot.NotFound", "Blocked slot not found."));
        }

        var venue = await _venueRepository.GetByIdAsync(slot.VenueId, cancellationToken);
        if (venue is null || venue.OwnerId != command.UserId)
        {
            return Result.Failure(new Error("BlockedSlot.Forbidden", "You are not authorized to unblock this slot."));
        }

        slot.MarkDeleted(_clock.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            action: "BlockedSlot.Deleted",
            entityName: nameof(BlockedSlot),
            entityId: slot.Id,
            userId: command.UserId,
            metadata: new { slot.VenueId, slot.PitchId, slot.StartUtc, slot.EndUtc },
            cancellationToken);

        _logger.LogInformation("Blocked slot {BlockedSlotId} deleted by user {UserId}", slot.Id, command.UserId);

        return Result.Success();
    }
}
