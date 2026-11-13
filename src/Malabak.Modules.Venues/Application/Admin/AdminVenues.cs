using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Admin;

public sealed record AdminVenueSearch(string? Query, string? ApprovalStatus, Guid? OwnerId, int Page = 1, int PageSize = 25);

public sealed record AdminVenueListItem(
    Guid Id, string Name, string City, string Area, Guid OwnerId, string ApprovalStatus, bool IsPublished, int Pitches, DateTime CreatedAtUtc);

public sealed record AdminVenuePage(IReadOnlyList<AdminVenueListItem> Items, int Page, int PageSize, int Total);

public sealed record AdminPitchItem(Guid Id, string Name, string TurfType, int Capacity, bool IsActive, IReadOnlyList<string>? Photos = null);

public sealed record AdminVenueEventItem(string Action, string FromStatus, string ToStatus, Guid ActorId, string? Message, DateTime OccurredAtUtc);

public sealed record AdminVenueDetail(
    Guid Id,
    string Name,
    string Slug,
    string Address,
    string City,
    string Area,
    string PhoneNumber,
    Guid OwnerId,
    string? OwnerName,
    string ApprovalStatus,
    bool IsPublished,
    string? AdminNote,
    int StaffCount,
    IReadOnlyList<AdminPitchItem> Pitches,
    VenueBookingStats Last30Days,
    IReadOnlyList<AdminVenueEventItem> History,
    DateTime CreatedAtUtc);

public static class AdminVenueErrors
{
    public static readonly Error NotFound = Error.NotFound("AdminVenues.NotFound", "Venue not found.");
    public static readonly Error InvalidFilter = Error.Validation("AdminVenues.InvalidFilter", "Invalid venue search filter.");
    public static readonly Error ReasonRequired = Error.Validation("AdminVenues.ReasonRequired", "A reason is required for this action.");
    public static readonly Error NoteTooLong = Error.Validation("AdminVenues.NoteTooLong", $"The admin note cannot exceed {Venue.MaxAdminNoteLength} characters.");
    public static readonly Error OwnerNotFound = Error.Validation("AdminVenues.OwnerNotFound", "The new owner must be an existing, active account.");
}

/// <summary>
/// Admin venue management (TASK-702): list and inspect venues with pitch inventory and booking figures, keep an internal
/// note, suspend/reactivate, ask the owner for information and move ownership. Every change is audited and the owner is
/// told about decisions that affect them.
/// </summary>
public sealed class AdminVenueService
{
    public const int MaxPageSize = 100;
    public const int MaxReasonLength = 1000;

    private readonly VenuesDbContext _dbContext;
    private readonly IVenueBookingStatsReader _stats;
    private readonly IUserAccountsContract _accounts;
    private readonly INotificationPublisher _notifications;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public AdminVenueService(
        VenuesDbContext dbContext,
        IVenueBookingStatsReader stats,
        IUserAccountsContract accounts,
        INotificationPublisher notifications,
        IAuditLog audit,
        IClock clock)
    {
        _dbContext = dbContext;
        _stats = stats;
        _accounts = accounts;
        _notifications = notifications;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<AdminVenuePage>> SearchAsync(AdminVenueSearch search, CancellationToken cancellationToken = default)
    {
        VenueApprovalStatus? status = null;
        if (search.ApprovalStatus is not null)
        {
            if (!Enum.TryParse<VenueApprovalStatus>(search.ApprovalStatus, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Result.Failure<AdminVenuePage>(AdminVenueErrors.InvalidFilter);
            }

            status = parsed;
        }

        if (search.Page < 1 || search.PageSize is < 1 or > MaxPageSize || search.Query?.Length > 100)
        {
            return Result.Failure<AdminVenuePage>(AdminVenueErrors.InvalidFilter);
        }

        var venues = _dbContext.Venues.AsNoTracking().Where(v => !v.IsDeleted);
        if (status is { } s)
        {
            venues = venues.Where(v => v.ApprovalStatus == s);
        }

        if (search.OwnerId is { } ownerId)
        {
            venues = venues.Where(v => v.OwnerId == ownerId);
        }

        if (!string.IsNullOrWhiteSpace(search.Query))
        {
            var term = search.Query.Trim().ToLower();
            venues = venues.Where(v => v.Name.ToLower().Contains(term) || v.City.ToLower().Contains(term) || v.Area.ToLower().Contains(term));
        }

        var total = await venues.CountAsync(cancellationToken);
        var rows = await venues.OrderByDescending(v => v.CreatedAtUtc)
            .Skip((search.Page - 1) * search.PageSize).Take(search.PageSize)
            .Select(v => new
            {
                v.Id,
                v.Name,
                v.City,
                v.Area,
                v.OwnerId,
                v.ApprovalStatus,
                v.IsPublished,
                v.CreatedAtUtc,
                Pitches = _dbContext.Pitches.Count(p => p.VenueId == v.Id && !p.IsDeleted)
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(v => new AdminVenueListItem(v.Id, v.Name, v.City, v.Area, v.OwnerId, v.ApprovalStatus.ToString(), v.IsPublished, v.Pitches, v.CreatedAtUtc)).ToList();
        return Result.Success(new AdminVenuePage(items, search.Page, search.PageSize, total));
    }

    public async Task<Result<AdminVenueDetail>> GetAsync(Guid venueId, CancellationToken cancellationToken = default)
    {
        var venue = await _dbContext.Venues.AsNoTracking().FirstOrDefaultAsync(v => v.Id == venueId && !v.IsDeleted, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<AdminVenueDetail>(AdminVenueErrors.NotFound);
        }

        var pitches = await _dbContext.Pitches.AsNoTracking().Where(p => p.VenueId == venueId && !p.IsDeleted).OrderBy(p => p.Name)
            .Select(p => new AdminPitchItem(p.Id, p.Name, p.TurfType, p.Capacity, p.IsActive, p.Photos.ToList())).ToListAsync(cancellationToken);
        var staff = await _dbContext.StaffMembers.AsNoTracking().CountAsync(m => m.VenueId == venueId && !m.IsDeleted, cancellationToken);
        var history = await _dbContext.ApprovalEvents.AsNoTracking().Where(e => e.VenueId == venueId).OrderByDescending(e => e.CreatedAtUtc).Take(50)
            .ToListAsync(cancellationToken);
        var now = _clock.UtcNow;
        var stats = await _stats.GetAsync(venueId, now.AddDays(-30), now, cancellationToken);
        var names = await _accounts.GetFullNamesAsync([venue.OwnerId], cancellationToken);

        return Result.Success(new AdminVenueDetail(
            venue.Id, venue.Name, venue.Slug, venue.Address, venue.City, venue.Area, venue.PhoneNumber, venue.OwnerId,
            names.GetValueOrDefault(venue.OwnerId), venue.ApprovalStatus.ToString(), venue.IsPublished, venue.AdminNote, staff, pitches, stats,
            history.Select(e => new AdminVenueEventItem(e.Action.ToString(), e.FromStatus.ToString(), e.ToStatus.ToString(), e.ActorUserId, e.Message, e.CreatedAtUtc)).ToList(),
            venue.CreatedAtUtc));
    }

    public async Task<Result<AdminVenueDetail>> SetAdminNoteAsync(Guid venueId, string? note, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (note?.Trim().Length > Venue.MaxAdminNoteLength)
        {
            return Result.Failure<AdminVenueDetail>(AdminVenueErrors.NoteTooLong);
        }

        var venue = await _dbContext.Venues.FirstOrDefaultAsync(v => v.Id == venueId && !v.IsDeleted, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<AdminVenueDetail>(AdminVenueErrors.NotFound);
        }

        var before = venue.AdminNote;
        venue.SetAdminNote(note, _clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync(new AuditEntry("admin.venue.note_updated", nameof(Venue), venueId.ToString("D"), adminId, AuditActorType.Admin,
            Before: new { AdminNote = before }, After: new { venue.AdminNote }), cancellationToken);
        return await GetAsync(venueId, cancellationToken);
    }

    public Task<Result<AdminVenueDetail>> SuspendAsync(Guid venueId, string? reason, Guid adminId, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(venueId, reason, adminId, (v, r, now) => v.Suspend(adminId, r, now), "admin.venue.suspended", NotificationTypes.VenueSuspended, cancellationToken);

    public Task<Result<AdminVenueDetail>> ReactivateAsync(Guid venueId, string? reason, Guid adminId, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(venueId, reason, adminId, (v, r, now) => v.Reactivate(adminId, r, now), "admin.venue.reactivated", NotificationTypes.VenueReactivated, cancellationToken);

    /// <summary>Asks the owner of a live venue for information (documents, corrections) without changing its status.</summary>
    public async Task<Result<AdminVenueDetail>> RequestInfoAsync(Guid venueId, string? message, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Trim().Length > MaxReasonLength)
        {
            return Result.Failure<AdminVenueDetail>(AdminVenueErrors.ReasonRequired);
        }

        var venue = await _dbContext.Venues.AsNoTracking().FirstOrDefaultAsync(v => v.Id == venueId && !v.IsDeleted, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<AdminVenueDetail>(AdminVenueErrors.NotFound);
        }

        await _notifications.PublishAsync(new NotificationRequest(venue.OwnerId, NotificationTypes.VenueInfoRequested,
            new Dictionary<string, string> { ["venueId"] = venue.Id.ToString("D"), ["venueName"] = venue.Name, ["message"] = message.Trim() },
            $"{NotificationTypes.VenueInfoRequested}:{venue.Id:N}:{_clock.UtcNow:yyyyMMddHHmmss}"), cancellationToken);
        await _audit.RecordAsync(new AuditEntry("admin.venue.info_requested", nameof(Venue), venueId.ToString("D"), adminId, AuditActorType.Admin,
            Reason: message.Trim()), cancellationToken);
        return await GetAsync(venueId, cancellationToken);
    }

    /// <summary>
    /// Moves the venue to another existing account, which gets the VenueOwner role. If the new owner worked there as staff,
    /// that staff seat is removed (owners have every venue permission anyway).
    /// </summary>
    public async Task<Result<AdminVenueDetail>> TransferOwnershipAsync(Guid venueId, Guid newOwnerId, string? reason, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaxReasonLength)
        {
            return Result.Failure<AdminVenueDetail>(AdminVenueErrors.ReasonRequired);
        }

        var venue = await _dbContext.Venues.FirstOrDefaultAsync(v => v.Id == venueId && !v.IsDeleted, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<AdminVenueDetail>(AdminVenueErrors.NotFound);
        }

        var names = await _accounts.GetFullNamesAsync([newOwnerId], cancellationToken);
        if (!names.ContainsKey(newOwnerId))
        {
            return Result.Failure<AdminVenueDetail>(AdminVenueErrors.OwnerNotFound);
        }

        var previousOwner = venue.OwnerId;
        var now = _clock.UtcNow;
        var transfer = venue.TransferOwnership(newOwnerId, adminId, reason, now);
        if (transfer.IsFailure)
        {
            return Result.Failure<AdminVenueDetail>(transfer.Error);
        }

        _dbContext.ApprovalEvents.Add(transfer.Value);
        var seats = await _dbContext.StaffMembers.Where(m => m.VenueId == venueId && m.UserId == newOwnerId && !m.IsDeleted).ToListAsync(cancellationToken);
        seats.ForEach(m => m.Remove(now));

        await _accounts.GrantRoleAsync(newOwnerId, "VenueOwner", cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _notifications.PublishAsync(new NotificationRequest(newOwnerId, NotificationTypes.VenueOwnershipReceived,
            new Dictionary<string, string> { ["venueId"] = venue.Id.ToString("D"), ["venueName"] = venue.Name }, $"{NotificationTypes.VenueOwnershipReceived}:{transfer.Value.Id:N}"), cancellationToken);
        await _audit.RecordAsync(new AuditEntry("admin.venue.ownership_transferred", nameof(Venue), venueId.ToString("D"), adminId, AuditActorType.Admin,
            Before: new { OwnerId = previousOwner }, After: new { OwnerId = newOwnerId }, Reason: reason.Trim()), cancellationToken);
        return await GetAsync(venueId, cancellationToken);
    }

    private async Task<Result<AdminVenueDetail>> ChangeStatusAsync(
        Guid venueId, string? reason, Guid adminId, Func<Venue, string, DateTime, Result<VenueApprovalEvent>> change, string action, string notificationType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaxReasonLength)
        {
            return Result.Failure<AdminVenueDetail>(AdminVenueErrors.ReasonRequired);
        }

        var venue = await _dbContext.Venues.FirstOrDefaultAsync(v => v.Id == venueId && !v.IsDeleted, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<AdminVenueDetail>(AdminVenueErrors.NotFound);
        }

        var before = new { Status = venue.ApprovalStatus.ToString(), venue.IsPublished };
        var result = change(venue, reason.Trim(), _clock.UtcNow);
        if (result.IsFailure)
        {
            return Result.Failure<AdminVenueDetail>(result.Error);
        }

        _dbContext.ApprovalEvents.Add(result.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _notifications.PublishAsync(new NotificationRequest(venue.OwnerId, notificationType,
            new Dictionary<string, string> { ["venueId"] = venue.Id.ToString("D"), ["venueName"] = venue.Name, ["reason"] = reason.Trim() },
            $"{notificationType}:{result.Value.Id:N}"), cancellationToken);
        await _audit.RecordAsync(new AuditEntry(action, nameof(Venue), venueId.ToString("D"), adminId, AuditActorType.Admin,
            Before: before, After: new { Status = venue.ApprovalStatus.ToString(), venue.IsPublished }, Reason: reason.Trim()), cancellationToken);
        return await GetAsync(venueId, cancellationToken);
    }
}
