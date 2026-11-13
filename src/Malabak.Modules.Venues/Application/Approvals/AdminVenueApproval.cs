using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Approvals;

/// <summary>
/// Admin decision on a submitted venue. Authorization is the admin.venues.approve policy at the endpoint;
/// the full approval queue UI and moderation tools come with the admin dashboard (Phase 6).
/// </summary>
public sealed record DecideVenueApprovalCommand(Guid VenueId, Guid AdminId, VenueApprovalDecision Decision, string? Message)
    : ICommand<Result<VenueApprovalDto>>;

public sealed class DecideVenueApprovalHandler : ICommandHandler<DecideVenueApprovalCommand, Result<VenueApprovalDto>>
{
    private readonly IVenueAuditLogger _auditLogger;
    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly INotificationPublisher? _notifications;

    public DecideVenueApprovalHandler(IVenueAuditLogger auditLogger, VenuesDbContext dbContext, IClock clock, INotificationPublisher? notifications = null)
    {
        _auditLogger = auditLogger;
        _dbContext = dbContext;
        _clock = clock;
        _notifications = notifications;
    }

    public async Task<Result<VenueApprovalDto>> HandleAsync(DecideVenueApprovalCommand command, CancellationToken cancellationToken = default)
    {
        var venue = await _dbContext.Venues.FirstOrDefaultAsync(v => v.Id == command.VenueId && !v.IsDeleted, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<VenueApprovalDto>(VenueAccessErrors.VenueNotFound);
        }

        var nowUtc = _clock.UtcNow;
        var transition = command.Decision switch
        {
            VenueApprovalDecision.Approve => venue.Approve(command.AdminId, command.Message, nowUtc),
            VenueApprovalDecision.Reject => venue.Reject(command.AdminId, command.Message ?? string.Empty, nowUtc),
            VenueApprovalDecision.RequestInfo => venue.RequestMoreInfo(command.AdminId, command.Message ?? string.Empty, nowUtc),
            _ => Result.Failure<VenueApprovalEvent>(new Error("VenueApproval.InvalidDecision", "Unknown decision."))
        };

        if (transition.IsFailure)
        {
            return Result.Failure<VenueApprovalDto>(transition.Error);
        }

        _dbContext.ApprovalEvents.Add(transition.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync($"Venue.Approval.{command.Decision}", nameof(Venue), venue.Id, command.AdminId,
            new { From = transition.Value.FromStatus.ToString(), To = transition.Value.ToStatus.ToString() }, cancellationToken);

        await NotifyOwnerAsync(venue, transition.Value, cancellationToken);

        return Result.Success(await VenueApprovalQueries.LoadAsync(_dbContext, venue, cancellationToken));
    }

    private async Task NotifyOwnerAsync(Venue venue, VenueApprovalEvent decision, CancellationToken cancellationToken)
    {
        if (_notifications is null)
        {
            return;
        }

        var type = decision.Action switch
        {
            VenueApprovalAction.Approved => NotificationTypes.VenueApproved,
            VenueApprovalAction.Rejected => NotificationTypes.VenueRejected,
            _ => NotificationTypes.VenueInfoRequested
        };

        var data = new Dictionary<string, string>
        {
            ["venueId"] = venue.Id.ToString("D"),
            ["venueName"] = venue.Name,
            ["reason"] = decision.Message ?? string.Empty,
            ["message"] = decision.Message ?? string.Empty
        };

        await _notifications.PublishAsync(new NotificationRequest(venue.OwnerId, type, data, $"{type}:{decision.Id:N}"), cancellationToken);
    }
}

public sealed record GetPendingVenueApprovalsQuery(int Page, int PageSize) : IQuery<Result<IReadOnlyList<PendingVenueApprovalDto>>>;

public sealed class GetPendingVenueApprovalsHandler : IQueryHandler<GetPendingVenueApprovalsQuery, Result<IReadOnlyList<PendingVenueApprovalDto>>>
{
    public const int MaxPageSize = 50;

    private readonly VenuesDbContext _dbContext;
    private readonly IUserAccountsContract _accounts;

    public GetPendingVenueApprovalsHandler(VenuesDbContext dbContext, IUserAccountsContract accounts)
    {
        _dbContext = dbContext;
        _accounts = accounts;
    }

    public async Task<Result<IReadOnlyList<PendingVenueApprovalDto>>> HandleAsync(GetPendingVenueApprovalsQuery query, CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var page = Math.Max(query.Page, 1);

        // Oldest submissions first so nothing waits indefinitely.
        var pending = await _dbContext.Venues.AsNoTracking()
            .Where(v => v.ApprovalStatus == VenueApprovalStatus.Submitted && !v.IsDeleted)
            .Select(v => new
            {
                Venue = v,
                PitchCount = v.Pitches.Count(p => !p.IsDeleted),
                Submission = _dbContext.ApprovalEvents
                    .Where(e => e.VenueId == v.Id && e.Action == VenueApprovalAction.Submitted)
                    .OrderByDescending(e => e.CreatedAtUtc)
                    .Select(e => new { e.CreatedAtUtc, e.Message })
                    .FirstOrDefault(),
            })
            .OrderBy(p => p.Submission!.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var venueIds = pending.Select(p => p.Venue.Id).ToList();
        var pitches = await _dbContext.Pitches.AsNoTracking()
            .Where(p => venueIds.Contains(p.VenueId) && !p.IsDeleted)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        var pitchesByVenue = pitches
            .GroupBy(p => p.VenueId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PendingApprovalPitchDto>)g.Select(p => new PendingApprovalPitchDto(
                    p.Id, p.Name, p.TurfType, p.Capacity, p.LengthMeters, p.WidthMeters, p.Photos.ToList()
                )).ToList()
            );

        var owners = await _accounts.GetFullNamesAsync(pending.Select(p => p.Venue.OwnerId).Distinct().ToList(), cancellationToken);

        IReadOnlyList<PendingVenueApprovalDto> result = pending
            .Select(p => new PendingVenueApprovalDto(
                p.Venue.Id, p.Venue.Name, p.Venue.City, p.Venue.Area, p.Venue.OwnerId, p.Submission?.CreatedAtUtc,
                p.Venue.Address, p.Venue.PhoneNumber, p.Venue.Description, p.Venue.Facilities.ToList(), p.Venue.Photos.ToList(),
                p.PitchCount, owners.GetValueOrDefault(p.Venue.OwnerId), p.Submission?.Message,
                pitchesByVenue.GetValueOrDefault(p.Venue.Id) ?? Array.Empty<PendingApprovalPitchDto>()))
            .ToList();

        return Result.Success(result);
    }
}
