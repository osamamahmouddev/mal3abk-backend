using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Approvals;

public sealed record SubmitVenueForApprovalCommand(Guid VenueId, Guid UserId, string? Message) : ICommand<Result<VenueApprovalDto>>;

/// <summary>Owner side: first submission, resubmission after rejection, or reply to an info request.</summary>
public sealed class SubmitVenueForApprovalHandler : ICommandHandler<SubmitVenueForApprovalCommand, Result<VenueApprovalDto>>
{
    private readonly IVenueAccessService _accessService;
    private readonly IVenueAuditLogger _auditLogger;
    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;

    public SubmitVenueForApprovalHandler(IVenueAccessService accessService, IVenueAuditLogger auditLogger, VenuesDbContext dbContext, IClock clock)
    {
        _accessService = accessService;
        _auditLogger = auditLogger;
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<VenueApprovalDto>> HandleAsync(SubmitVenueForApprovalCommand command, CancellationToken cancellationToken = default)
    {
        var access = await _accessService.GetAccessAsync(command.UserId, command.VenueId, cancellationToken);
        if (!access.Can(Permissions.VenueEdit))
        {
            return Result.Failure<VenueApprovalDto>(VenueAccessErrors.VenueNotFound);
        }

        var venue = await _dbContext.Venues.FirstOrDefaultAsync(v => v.Id == command.VenueId && !v.IsDeleted, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<VenueApprovalDto>(VenueAccessErrors.VenueNotFound);
        }

        if (!await _dbContext.Pitches.AnyAsync(p => p.VenueId == venue.Id && !p.IsDeleted, cancellationToken))
        {
            return Result.Failure<VenueApprovalDto>(VenueApprovalErrors.NoPitches);
        }

        var transition = venue.SubmitForApproval(command.UserId, command.Message, _clock.UtcNow);
        if (transition.IsFailure)
        {
            return Result.Failure<VenueApprovalDto>(transition.Error);
        }

        _dbContext.ApprovalEvents.Add(transition.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync("Venue.SubmittedForApproval", nameof(Venue), venue.Id, command.UserId,
            new { From = transition.Value.FromStatus.ToString() }, cancellationToken);

        return Result.Success(await VenueApprovalQueries.LoadAsync(_dbContext, venue, cancellationToken));
    }
}

public sealed record GetVenueApprovalQuery(Guid VenueId, Guid UserId) : IQuery<Result<VenueApprovalDto>>;

public sealed class GetVenueApprovalHandler : IQueryHandler<GetVenueApprovalQuery, Result<VenueApprovalDto>>
{
    private readonly IVenueAccessService _accessService;
    private readonly VenuesDbContext _dbContext;

    public GetVenueApprovalHandler(IVenueAccessService accessService, VenuesDbContext dbContext)
    {
        _accessService = accessService;
        _dbContext = dbContext;
    }

    public async Task<Result<VenueApprovalDto>> HandleAsync(GetVenueApprovalQuery query, CancellationToken cancellationToken = default)
    {
        var access = await _accessService.GetAccessAsync(query.UserId, query.VenueId, cancellationToken);
        if (!access.Can(Permissions.VenueView))
        {
            return Result.Failure<VenueApprovalDto>(VenueAccessErrors.VenueNotFound);
        }

        var venue = await _dbContext.Venues.AsNoTracking().FirstOrDefaultAsync(v => v.Id == query.VenueId && !v.IsDeleted, cancellationToken);
        return venue is null
            ? Result.Failure<VenueApprovalDto>(VenueAccessErrors.VenueNotFound)
            : Result.Success(await VenueApprovalQueries.LoadAsync(_dbContext, venue, cancellationToken));
    }
}

internal static class VenueApprovalQueries
{
    public static async Task<VenueApprovalDto> LoadAsync(VenuesDbContext db, Venue venue, CancellationToken cancellationToken)
    {
        var history = await db.ApprovalEvents.AsNoTracking()
            .Where(e => e.VenueId == venue.Id)
            .OrderBy(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return new VenueApprovalDto(
            venue.Id, venue.ApprovalStatus.ToString(), venue.IsPublished, venue.IsSearchable,
            history.Select(VenueApprovalMapping.ToDto).ToList());
    }
}
