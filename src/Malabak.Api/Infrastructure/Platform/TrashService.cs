using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Api.Infrastructure.Platform;

/// <summary>Something the platform team deleted from the admin console: who, when and why, until it is restored or purged.</summary>
public sealed class TrashEntry
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid DeletedByUserId { get; set; }
    public DateTime DeletedAtUtc { get; set; }
}

public sealed record TrashItemDto(
    Guid Id,
    string Kind,
    Guid EntityId,
    string Title,
    string? Subtitle,
    string Reason,
    Guid DeletedByUserId,
    string? DeletedByName,
    DateTime DeletedAtUtc,
    DateTime PurgeAtUtc);

public sealed record TrashPurgeAllResult(int Purged, int Failed);

/// <summary>
/// The admin trash ("المحذوف مؤخرا"). Each kind is deleted, restored and purged by its own module (<see cref="ITrashProvider"/>);
/// this keeps the list, enforces the confirmations SECURITY.md asks for (a reason, step-up at the endpoint, and typing the
/// item's name before anything irreversible), audits every step and purges what has been in the trash past the retention.
/// </summary>
public sealed class TrashService
{
    /// <summary>What the admin types to empty the whole trash.</summary>
    public const string EmptyAllConfirmation = "مسح الكل";

    private readonly PlatformDbContext _dbContext;
    private readonly IReadOnlyDictionary<string, ITrashProvider> _providers;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;
    private readonly IUserAccountsContract _users;
    private readonly ILogger<TrashService> _logger;

    public TrashService(
        PlatformDbContext dbContext, IEnumerable<ITrashProvider> providers, IAuditLog audit, IClock clock, IUserAccountsContract users,
        ILogger<TrashService> logger)
    {
        _dbContext = dbContext;
        _providers = providers.ToDictionary(p => p.Kind, StringComparer.Ordinal);
        _audit = audit;
        _clock = clock;
        _users = users;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TrashItemDto>> ListAsync(string? kind, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.TrashEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(kind))
        {
            query = query.Where(e => e.Kind == kind);
        }

        var entries = await query.OrderByDescending(e => e.DeletedAtUtc).Take(500).ToListAsync(cancellationToken);
        var names = await _users.GetFullNamesAsync(entries.Select(e => e.DeletedByUserId).Distinct().ToList(), cancellationToken);
        return entries
            .Select(e => new TrashItemDto(e.Id, e.Kind, e.EntityId, e.Title, e.Subtitle, e.Reason, e.DeletedByUserId,
                names.GetValueOrDefault(e.DeletedByUserId), e.DeletedAtUtc, e.DeletedAtUtc + TrashRules.Retention))
            .ToList();
    }

    public async Task<Result<TrashItemDto>> MoveToTrashAsync(
        string kind, Guid id, string? reason, string? confirmName, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (!_providers.TryGetValue(kind, out var provider))
        {
            return Result.Failure<TrashItemDto>(TrashErrors.UnknownKind);
        }

        var cleanReason = reason?.Trim() ?? string.Empty;
        if (cleanReason.Length is < 5 or > 500)
        {
            return Result.Failure<TrashItemDto>(TrashErrors.ReasonRequired);
        }

        // Deleting a venue is one of the dangerous actions SECURITY.md asks to confirm by typing its name.
        if (kind == "venue")
        {
            var name = await provider.GetNameAsync(id, cancellationToken);
            if (name is null)
            {
                return Result.Failure<TrashItemDto>(TrashErrors.NotFound);
            }

            if (!SameName(confirmName, name))
            {
                return Result.Failure<TrashItemDto>(TrashErrors.ConfirmationMismatch);
            }
        }

        var moved = await provider.MoveToTrashAsync(id, adminId, cancellationToken);
        if (moved.IsFailure)
        {
            return Result.Failure<TrashItemDto>(moved.Error);
        }

        var entry = new TrashEntry
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            EntityId = id,
            Title = Truncate(moved.Value.Title, 200),
            Subtitle = moved.Value.Subtitle is null ? null : Truncate(moved.Value.Subtitle, 200),
            Reason = cleanReason,
            DeletedByUserId = adminId,
            DeletedAtUtc = _clock.UtcNow,
        };
        _dbContext.TrashEntries.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await AuditAsync("admin.trash.deleted", entry, adminId, AuditActorType.Admin, cleanReason, cancellationToken);
        var names = await _users.GetFullNamesAsync([adminId], cancellationToken);
        return Result.Success(new TrashItemDto(entry.Id, kind, id, entry.Title, entry.Subtitle, entry.Reason, adminId,
            names.GetValueOrDefault(adminId), entry.DeletedAtUtc, entry.DeletedAtUtc + TrashRules.Retention));
    }

    public async Task<Result> RestoreAsync(Guid entryId, Guid adminId, CancellationToken cancellationToken = default)
    {
        var entry = await _dbContext.TrashEntries.FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);
        if (entry is null || !_providers.TryGetValue(entry.Kind, out var provider))
        {
            return Result.Failure(TrashErrors.NotFound);
        }

        var restored = await provider.RestoreAsync(entry.EntityId, cancellationToken);
        if (restored.IsFailure)
        {
            return restored;
        }

        _dbContext.TrashEntries.Remove(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AuditAsync("admin.trash.restored", entry, adminId, AuditActorType.Admin, null, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> PurgeAsync(Guid entryId, string? confirmName, Guid adminId, CancellationToken cancellationToken = default)
    {
        var entry = await _dbContext.TrashEntries.FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);
        if (entry is null)
        {
            return Result.Failure(TrashErrors.NotFound);
        }

        if (!SameName(confirmName, entry.Title))
        {
            return Result.Failure(TrashErrors.ConfirmationMismatch);
        }

        return await PurgeEntryAsync(entry, adminId, AuditActorType.Admin, cancellationToken);
    }

    public async Task<Result<TrashPurgeAllResult>> PurgeAllAsync(string? confirmation, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(confirmation?.Trim(), EmptyAllConfirmation, StringComparison.Ordinal))
        {
            return Result.Failure<TrashPurgeAllResult>(TrashErrors.ConfirmationMismatch);
        }

        var entries = await _dbContext.TrashEntries.OrderBy(e => e.DeletedAtUtc).ToListAsync(cancellationToken);
        return Result.Success(await PurgeManyAsync(entries, adminId, AuditActorType.Admin, cancellationToken));
    }

    /// <summary>Daily: what has been in the trash past the retention is purged for good (SECURITY.md: personal data erased after a period).</summary>
    public async Task<TrashPurgeAllResult> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = _clock.UtcNow - TrashRules.Retention;
        var entries = await _dbContext.TrashEntries.Where(e => e.DeletedAtUtc <= cutoff).OrderBy(e => e.DeletedAtUtc).Take(200).ToListAsync(cancellationToken);
        return await PurgeManyAsync(entries, null, AuditActorType.System, cancellationToken);
    }

    private async Task<TrashPurgeAllResult> PurgeManyAsync(List<TrashEntry> entries, Guid? actorId, string actorType, CancellationToken cancellationToken)
    {
        var purged = 0;
        var failed = 0;
        foreach (var entry in entries)
        {
            var result = await PurgeEntryAsync(entry, actorId, actorType, cancellationToken);
            if (result.IsSuccess)
            {
                purged++;
            }
            else
            {
                failed++;
            }
        }

        return new TrashPurgeAllResult(purged, failed);
    }

    private async Task<Result> PurgeEntryAsync(TrashEntry entry, Guid? actorId, string actorType, CancellationToken cancellationToken)
    {
        if (!_providers.TryGetValue(entry.Kind, out var provider))
        {
            return Result.Failure(TrashErrors.UnknownKind);
        }

        Result purged;
        try
        {
            purged = await provider.PurgeAsync(entry.EntityId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One item failing must not stop the rest; it stays in the trash and is tried again.
            _logger.LogError(ex, "Purging {Kind} {EntityId} from the trash failed", entry.Kind, entry.EntityId);
            return Result.Failure(TrashErrors.NotFound);
        }

        // Already gone from its module (purged elsewhere): the entry is stale either way.
        if (purged.IsFailure && purged.Error != TrashErrors.NotFound)
        {
            return purged;
        }

        _dbContext.TrashEntries.Remove(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AuditAsync("admin.trash.purged", entry, actorId, actorType, null, cancellationToken);
        return Result.Success();
    }

    private Task AuditAsync(string action, TrashEntry entry, Guid? actorId, string actorType, string? reason, CancellationToken cancellationToken) =>
        _audit.RecordAsync(new AuditEntry(action, entry.Kind, entry.EntityId.ToString("D"), actorId, actorType,
            After: new { entry.Kind, entry.DeletedAtUtc }, Reason: reason), cancellationToken);

    private static bool SameName(string? typed, string title) =>
        string.Equals(typed?.Trim(), title.Trim(), StringComparison.Ordinal);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
