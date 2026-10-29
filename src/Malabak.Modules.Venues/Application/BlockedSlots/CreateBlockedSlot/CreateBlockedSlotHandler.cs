using Malabak.Modules.Venues.Application.BlockedSlots;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Venues.Application.BlockedSlots.CreateBlockedSlot;

public sealed class CreateBlockedSlotHandler : ICommandHandler<CreateBlockedSlotCommand, Result<BlockedSlotDto>>
{
    private const int _maxReasonLength = 500;

    private readonly IVenueRepository _venueRepository;
    private readonly IPitchRepository _pitchRepository;
    private readonly IBlockedSlotRepository _blockedSlotRepository;
    private readonly IBookingConflictChecker _conflictChecker;
    private readonly IVenueAuditLogger _auditLogger;
    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<CreateBlockedSlotHandler> _logger;

    public CreateBlockedSlotHandler(
        IVenueRepository venueRepository,
        IPitchRepository pitchRepository,
        IBlockedSlotRepository blockedSlotRepository,
        IBookingConflictChecker conflictChecker,
        IVenueAuditLogger auditLogger,
        VenuesDbContext dbContext,
        IClock clock,
        ILogger<CreateBlockedSlotHandler> logger)
    {
        _venueRepository = venueRepository;
        _pitchRepository = pitchRepository;
        _blockedSlotRepository = blockedSlotRepository;
        _conflictChecker = conflictChecker;
        _auditLogger = auditLogger;
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<BlockedSlotDto>> HandleAsync(
        CreateBlockedSlotCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationError = Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<BlockedSlotDto>(validationError);
        }

        var venue = await _venueRepository.GetByIdAsync(command.VenueId, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<BlockedSlotDto>(new Error("Venue.NotFound", "Venue not found."));
        }

        if (!await VenuePermissionCheck.CanAsync(_dbContext, command.UserId, venue.Id, Permissions.BlockedSlotsManage, cancellationToken))
        {
            return Result.Failure<BlockedSlotDto>(new Error("Venue.Forbidden", "You are not authorized to block slots for this venue."));
        }

        var pitch = await _pitchRepository.GetByIdAsync(command.PitchId, cancellationToken);
        if (pitch is null || pitch.VenueId != venue.Id)
        {
            return Result.Failure<BlockedSlotDto>(new Error("Pitch.NotFound", "Pitch not found under this venue."));
        }

        var hasOverlap = await _blockedSlotRepository.HasOverlappingBlockAsync(
            command.PitchId,
            command.StartUtc,
            command.EndUtc,
            excludeId: null,
            cancellationToken);

        if (hasOverlap)
        {
            return Result.Failure<BlockedSlotDto>(new Error("BlockedSlot.Overlap", "An overlapping blocked slot already exists for this pitch."));
        }

        var hasBookingConflict = await _conflictChecker.HasActiveBookingsAsync(
            command.PitchId,
            command.StartUtc,
            command.EndUtc,
            cancellationToken);

        if (hasBookingConflict)
        {
            return Result.Failure<BlockedSlotDto>(new Error(
                "BlockedSlot.ConflictingBookings",
                "Cannot block slot: active customer bookings exist in the selected time window."));
        }

        var blockedSlot = BlockedSlot.Create(
            id: Guid.NewGuid(),
            venueId: venue.Id,
            pitchId: pitch.Id,
            startUtc: command.StartUtc,
            endUtc: command.EndUtc,
            reason: command.Reason,
            blockedByUserId: command.UserId,
            createdAtUtc: _clock.UtcNow);

        // Tracked through the repository only; adding it to Venue.BlockedSlots as well would make
        // the venue aggregate the owner of the slot's lifecycle and turn later removals into hard deletes.
        _blockedSlotRepository.Add(blockedSlot);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Reason is free text typed by the venue and may contain customer names/phones, so it is kept out of logs.
        await _auditLogger.LogAsync(
            action: "BlockedSlot.Created",
            entityName: nameof(BlockedSlot),
            entityId: blockedSlot.Id,
            userId: command.UserId,
            metadata: new
            {
                VenueId = venue.Id,
                PitchId = pitch.Id,
                command.StartUtc,
                command.EndUtc
            },
            cancellationToken);

        _logger.LogInformation(
            "Blocked slot {BlockedSlotId} created on pitch {PitchId} ({StartUtc} to {EndUtc}) by user {UserId}",
            blockedSlot.Id,
            pitch.Id,
            command.StartUtc,
            command.EndUtc,
            command.UserId);

        return Result.Success(BlockedSlotMapper.ToDto(blockedSlot));
    }

    private Error? Validate(CreateBlockedSlotCommand command)
    {
        if (command.StartUtc.Kind != DateTimeKind.Utc || command.EndUtc.Kind != DateTimeKind.Utc)
        {
            return new Error("BlockedSlot.InvalidTime", "StartUtc and EndUtc must be UTC timestamps.");
        }

        if (command.EndUtc <= command.StartUtc)
        {
            return new Error("BlockedSlot.InvalidTime", "EndUtc must be after StartUtc.");
        }

        if (command.EndUtc <= _clock.UtcNow)
        {
            return new Error("BlockedSlot.InPast", "Cannot block a time window that has already ended.");
        }

        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length > _maxReasonLength)
        {
            return new Error("BlockedSlot.InvalidReason", $"Reason is required and cannot exceed {_maxReasonLength} characters.");
        }

        return null;
    }
}
