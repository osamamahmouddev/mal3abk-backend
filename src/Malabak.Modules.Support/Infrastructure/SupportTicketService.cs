using Malabak.Modules.Support.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Support.Infrastructure;

public sealed record OpenTicketInput(string? Category, string? Subject, string? Body, Guid? RelatedBookingId, Guid? RelatedVenueId);

public sealed record TicketMessageDto(Guid Id, string AuthorKind, Guid AuthorId, string Body, bool IsInternal, DateTime CreatedAtUtc);

public sealed record TicketSummaryDto(
    Guid Id, string Number, string Category, string Priority, string Status, string Subject, string RequesterKind, Guid RequesterId,
    Guid? AssigneeId, Guid? RelatedBookingId, Guid? RelatedVenueId, DateTime CreatedAtUtc, DateTime LastActivityAtUtc);

public sealed record TicketDetailDto(TicketSummaryDto Ticket, string? ResolutionReason, DateTime? ResolvedAtUtc, IReadOnlyList<TicketMessageDto> Messages);

public sealed record TicketPage(IReadOnlyList<TicketSummaryDto> Items, int Page, int PageSize, int Total);

public sealed record TicketFilter(
    string? Status, string? Category, string? Priority, Guid? AssigneeId, bool UnassignedOnly, Guid? RequesterId, Guid? RelatedVenueId, Guid? RelatedBookingId,
    string? Query, int Page = 1, int PageSize = 25);

/// <summary>
/// Support tickets (TASK-706). Requesters only ever see their own tickets and never internal notes; agents see everything.
/// Agent actions (assignment, priority, resolution, closing) are audited; message bodies are not copied into the audit trail.
/// </summary>
public sealed class SupportTicketService : IVenueComplaintsReader
{
    public const int MaxPageSize = 100;
    public const int MaxOpenTicketsPerRequester = 10;

    private readonly SupportDbContext _dbContext;
    private readonly INotificationPublisher _notifications;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public SupportTicketService(SupportDbContext dbContext, INotificationPublisher notifications, IAuditLog audit, IClock clock)
    {
        _dbContext = dbContext;
        _notifications = notifications;
        _audit = audit;
        _clock = clock;
    }

    // ---- requester side ----

    public async Task<Result<TicketDetailDto>> OpenAsync(Guid requesterId, TicketRequesterKind kind, OpenTicketInput input, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<TicketCategory>(input.Category, ignoreCase: true, out var category) || !Enum.IsDefined(category))
        {
            return Result.Failure<TicketDetailDto>(SupportErrors.Invalid);
        }

        var open = await _dbContext.Tickets.CountAsync(
            t => t.RequesterId == requesterId && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed, cancellationToken);
        if (open >= MaxOpenTicketsPerRequester)
        {
            return Result.Failure<TicketDetailDto>(SupportErrors.TooManyOpen);
        }

        var ticket = SupportTicket.Open(requesterId, kind, category, input.Subject, input.Body, input.RelatedBookingId, input.RelatedVenueId, null, _clock.UtcNow);
        if (ticket.IsFailure)
        {
            return Result.Failure<TicketDetailDto>(ticket.Error);
        }

        _dbContext.Tickets.Add(ticket.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDetail(ticket.Value, includeInternal: false));
    }

    public async Task<TicketPage> ListMineAsync(Guid requesterId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var mine = _dbContext.Tickets.AsNoTracking().Where(t => t.RequesterId == requesterId);
        var total = await mine.CountAsync(cancellationToken);
        var rows = await mine.OrderByDescending(t => t.LastActivityAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new TicketPage(rows.Select(ToSummary).ToList(), page, pageSize, total);
    }

    public async Task<Result<TicketDetailDto>> GetMineAsync(Guid ticketId, Guid requesterId, CancellationToken cancellationToken = default)
    {
        var ticket = await LoadAsync(ticketId, tracking: false, cancellationToken);
        return ticket is null || ticket.RequesterId != requesterId
            ? Result.Failure<TicketDetailDto>(SupportErrors.NotFound)
            : Result.Success(ToDetail(ticket, includeInternal: false));
    }

    public async Task<Result<TicketDetailDto>> ReplyAsRequesterAsync(Guid ticketId, Guid requesterId, string? body, CancellationToken cancellationToken = default)
    {
        var ticket = await LoadAsync(ticketId, tracking: true, cancellationToken);
        if (ticket is null || ticket.RequesterId != requesterId)
        {
            return Result.Failure<TicketDetailDto>(SupportErrors.NotFound);
        }

        var reply = ticket.RequesterReply(requesterId, body, _clock.UtcNow);
        if (reply.IsFailure)
        {
            return Result.Failure<TicketDetailDto>(reply.Error);
        }

        _dbContext.Messages.Add(reply.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDetail(ticket, includeInternal: false));
    }

    // ---- agent side ----

    public async Task<Result<TicketPage>> SearchAsync(TicketFilter filter, CancellationToken cancellationToken = default)
    {
        if (filter.Page < 1 || filter.PageSize is < 1 or > MaxPageSize || filter.Query?.Length > 100
            || !TryParse<TicketStatus>(filter.Status, out var status) || !TryParse<TicketCategory>(filter.Category, out var category)
            || !TryParse<TicketPriority>(filter.Priority, out var priority))
        {
            return Result.Failure<TicketPage>(SupportErrors.InvalidFilter);
        }

        var tickets = _dbContext.Tickets.AsNoTracking();
        if (status is { } s)
        {
            tickets = tickets.Where(t => t.Status == s);
        }

        if (category is { } c)
        {
            tickets = tickets.Where(t => t.Category == c);
        }

        if (priority is { } p)
        {
            tickets = tickets.Where(t => t.Priority == p);
        }

        if (filter.UnassignedOnly)
        {
            tickets = tickets.Where(t => t.AssigneeId == null);
        }
        else if (filter.AssigneeId is { } assignee)
        {
            tickets = tickets.Where(t => t.AssigneeId == assignee);
        }

        if (filter.RequesterId is { } requester)
        {
            tickets = tickets.Where(t => t.RequesterId == requester);
        }

        if (filter.RelatedVenueId is { } venue)
        {
            tickets = tickets.Where(t => t.RelatedVenueId == venue);
        }

        if (filter.RelatedBookingId is { } booking)
        {
            tickets = tickets.Where(t => t.RelatedBookingId == booking);
        }

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var term = filter.Query.Trim().ToLower();
            tickets = tickets.Where(t => t.Number.ToLower().Contains(term) || t.Subject.ToLower().Contains(term));
        }

        var total = await tickets.CountAsync(cancellationToken);
        // Most urgent first, then the ones waiting longest for an answer.
        var rows = await tickets.OrderByDescending(t => t.Priority).ThenBy(t => t.LastActivityAtUtc)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync(cancellationToken);
        return Result.Success(new TicketPage(rows.Select(ToSummary).ToList(), filter.Page, filter.PageSize, total));
    }

    public async Task<Result<TicketDetailDto>> GetAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await LoadAsync(ticketId, tracking: false, cancellationToken);
        return ticket is null ? Result.Failure<TicketDetailDto>(SupportErrors.NotFound) : Result.Success(ToDetail(ticket, includeInternal: true));
    }

    public async Task<Result<TicketDetailDto>> ReplyAsAgentAsync(Guid ticketId, Guid agentId, string? body, bool isInternal, CancellationToken cancellationToken = default)
    {
        var ticket = await LoadAsync(ticketId, tracking: true, cancellationToken);
        if (ticket is null)
        {
            return Result.Failure<TicketDetailDto>(SupportErrors.NotFound);
        }

        var reply = ticket.AgentReply(agentId, body, isInternal, _clock.UtcNow);
        if (reply.IsFailure)
        {
            return Result.Failure<TicketDetailDto>(reply.Error);
        }

        _dbContext.Messages.Add(reply.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (!isInternal)
        {
            await NotifyAsync(ticket, NotificationTypes.SupportTicketReplied, reply.Value.Id.ToString("N"), new(), cancellationToken);
        }

        return Result.Success(ToDetail(ticket, includeInternal: true));
    }

    public Task<Result<TicketDetailDto>> AssignAsync(Guid ticketId, Guid? assigneeId, Guid agentId, CancellationToken cancellationToken = default) =>
        ChangeAsync(ticketId, agentId, "support.ticket.assigned", t => t.Assign(assigneeId, _clock.UtcNow), t => new { t.AssigneeId }, null, cancellationToken);

    public Task<Result<TicketDetailDto>> SetPriorityAsync(Guid ticketId, string? priority, Guid agentId, CancellationToken cancellationToken = default) =>
        Enum.TryParse<TicketPriority>(priority, ignoreCase: true, out var parsed)
            ? ChangeAsync(ticketId, agentId, "support.ticket.priority_changed", t => t.SetPriority(parsed, _clock.UtcNow), t => new { Priority = t.Priority.ToString() }, null, cancellationToken)
            : Task.FromResult(Result.Failure<TicketDetailDto>(SupportErrors.Invalid));

    public async Task<Result<TicketDetailDto>> ResolveAsync(Guid ticketId, string? reason, Guid agentId, CancellationToken cancellationToken = default)
    {
        var result = await ChangeAsync(ticketId, agentId, "support.ticket.resolved", t => t.Resolve(reason, _clock.UtcNow),
            t => new { Status = t.Status.ToString() }, reason, cancellationToken);
        if (result.IsSuccess)
        {
            var ticket = await LoadAsync(ticketId, tracking: false, cancellationToken);
            await NotifyAsync(ticket!, NotificationTypes.SupportTicketResolved, "resolved", new() { ["reason"] = ticket!.ResolutionReason ?? string.Empty }, cancellationToken);
        }

        return result;
    }

    public Task<Result<TicketDetailDto>> CloseAsync(Guid ticketId, string? reason, Guid agentId, CancellationToken cancellationToken = default) =>
        ChangeAsync(ticketId, agentId, "support.ticket.closed", t => t.Close(reason, _clock.UtcNow), t => new { Status = t.Status.ToString() }, reason, cancellationToken);

    /// <summary>Complaints about a venue (TASK-702): tickets in the venue-complaint category that name it.</summary>
    public async Task<IReadOnlyList<VenueComplaintDto>> GetComplaintsAsync(Guid venueId, int take, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.Tickets.AsNoTracking()
            .Where(t => t.RelatedVenueId == venueId && t.Category == TicketCategory.VenueComplaint)
            .OrderByDescending(t => t.CreatedAtUtc).Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);
        return rows.Select(t => new VenueComplaintDto(t.Id, t.Number, t.Subject, t.Status.ToString(), t.Priority.ToString(), t.RequesterId, t.CreatedAtUtc, t.ResolutionReason))
            .ToList();
    }

    private async Task<Result<TicketDetailDto>> ChangeAsync(
        Guid ticketId, Guid agentId, string action, Func<SupportTicket, Result> change, Func<SupportTicket, object> snapshot, string? reason,
        CancellationToken cancellationToken)
    {
        var ticket = await LoadAsync(ticketId, tracking: true, cancellationToken);
        if (ticket is null)
        {
            return Result.Failure<TicketDetailDto>(SupportErrors.NotFound);
        }

        var before = snapshot(ticket);
        var result = change(ticket);
        if (result.IsFailure)
        {
            return Result.Failure<TicketDetailDto>(result.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync(new AuditEntry(action, "SupportTicket", ticket.Id.ToString("D"), agentId, AuditActorType.Admin,
            Before: before, After: snapshot(ticket), Reason: reason?.Trim()), cancellationToken);
        return Result.Success(ToDetail(ticket, includeInternal: true));
    }

    private Task NotifyAsync(SupportTicket ticket, string type, string key, Dictionary<string, string> data, CancellationToken cancellationToken)
    {
        data["ticketId"] = ticket.Id.ToString("D");
        data["ticketNumber"] = ticket.Number;
        return _notifications.PublishAsync(new NotificationRequest(ticket.RequesterId, type, data, $"{type}:{ticket.Id:N}:{key}"), cancellationToken);
    }

    private Task<SupportTicket?> LoadAsync(Guid ticketId, bool tracking, CancellationToken cancellationToken)
    {
        var query = _dbContext.Tickets.Include(t => t.Messages).AsQueryable();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return query.FirstOrDefaultAsync(t => t.Id == ticketId, cancellationToken);
    }

    private static bool TryParse<TEnum>(string? value, out TEnum? parsed)
        where TEnum : struct, Enum
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var result) && Enum.IsDefined(result))
        {
            parsed = result;
            return true;
        }

        return false;
    }

    private static TicketSummaryDto ToSummary(SupportTicket t) =>
        new(t.Id, t.Number, t.Category.ToString(), t.Priority.ToString(), t.Status.ToString(), t.Subject, t.RequesterKind.ToString(), t.RequesterId,
            t.AssigneeId, t.RelatedBookingId, t.RelatedVenueId, t.CreatedAtUtc, t.LastActivityAtUtc);

    private static TicketDetailDto ToDetail(SupportTicket t, bool includeInternal) =>
        new(ToSummary(t), t.ResolutionReason, t.ResolvedAtUtc,
            t.Messages.Where(m => includeInternal || !m.IsInternal).OrderBy(m => m.CreatedAtUtc)
                .Select(m => new TicketMessageDto(m.Id, m.AuthorKind.ToString(), m.AuthorId, m.Body, m.IsInternal, m.CreatedAtUtc)).ToList());
}
