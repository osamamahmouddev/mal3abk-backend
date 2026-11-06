using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Audit.Infrastructure;

public sealed record AuditLogFilter(
    Guid? ActorId = null,
    string? Action = null,
    string? EntityType = null,
    string? EntityId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Page = 1,
    int PageSize = 50);

public sealed record AuditLogDto(
    Guid Id,
    DateTime OccurredAtUtc,
    Guid? ActorId,
    string ActorType,
    Guid? ImpersonatorId,
    string Action,
    string EntityType,
    string EntityId,
    string? BeforeJson,
    string? AfterJson,
    string? Reason,
    string? IpAddress,
    string? CorrelationId);

public sealed record AuditLogPage(IReadOnlyList<AuditLogDto> Items, int TotalCount, int Page, int PageSize);

public static class AuditLogErrors
{
    public static readonly Error InvalidFilter = new("Audit.InvalidFilter", "Check the filters: page size 1-100 and a start date before the end date.");
}

/// <summary>Audit log viewer (TASK-062): newest first, filtered by actor, action (prefix), entity and time.</summary>
public sealed class AuditLogQuery
{
    private readonly AuditDbContext _dbContext;

    public AuditLogQuery(AuditDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AuditLogPage>> SearchAsync(AuditLogFilter filter, CancellationToken cancellationToken = default)
    {
        if (filter.Page < 1 || filter.PageSize is < 1 or > 100 || (filter.FromUtc > filter.ToUtc)
            || filter.Action?.Length > 100 || filter.EntityType?.Length > 100 || filter.EntityId?.Length > 100)
        {
            return Result.Failure<AuditLogPage>(AuditLogErrors.InvalidFilter);
        }

        var query = _dbContext.Entries.AsNoTracking();
        if (filter.ActorId is { } actor)
        {
            query = query.Where(e => e.ActorId == actor || e.ImpersonatorId == actor);
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            var action = filter.Action.Trim();
            query = query.Where(e => e.Action.StartsWith(action));
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            query = query.Where(e => e.EntityType == filter.EntityType.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityId))
        {
            query = query.Where(e => e.EntityId == filter.EntityId.Trim());
        }

        if (filter.FromUtc is { } from)
        {
            query = query.Where(e => e.OccurredAtUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(e => e.OccurredAtUtc <= to);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(e => new AuditLogDto(e.Id, e.OccurredAtUtc, e.ActorId, e.ActorType, e.ImpersonatorId, e.Action, e.EntityType, e.EntityId,
                e.BeforeJson, e.AfterJson, e.Reason, e.IpAddress, e.CorrelationId))
            .ToListAsync(cancellationToken);
        return Result.Success(new AuditLogPage(items, total, filter.Page, filter.PageSize));
    }
}
