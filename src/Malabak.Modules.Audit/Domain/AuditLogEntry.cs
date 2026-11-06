namespace Malabak.Modules.Audit.Domain;

/// <summary>
/// One row of the append-only audit trail. Rows are only ever inserted: the database rejects UPDATE and DELETE on the
/// table (trigger plus revoked grants), so not even the application can rewrite history.
/// </summary>
public sealed class AuditLogEntry
{
    public const int MaxSnapshotLength = 16_000;

    public Guid Id { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public Guid? ActorId { get; private set; }
    public string ActorType { get; private set; } = string.Empty;
    public Guid? ImpersonatorId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public string? Reason { get; private set; }
    public string? IpAddress { get; private set; }
    public string? CorrelationId { get; private set; }

    private AuditLogEntry()
    {
    }

    public static AuditLogEntry Create(
        Guid id, DateTime occurredAtUtc, Guid? actorId, string actorType, Guid? impersonatorId, string action, string entityType, string entityId,
        string? beforeJson, string? afterJson, string? reason, string? ipAddress, string? correlationId) => new()
        {
            Id = id,
            OccurredAtUtc = occurredAtUtc,
            ActorId = actorId,
            ActorType = Truncate(actorType, 20)!,
            ImpersonatorId = impersonatorId,
            Action = Truncate(action, 100)!,
            EntityType = Truncate(entityType, 100)!,
            EntityId = Truncate(entityId, 100)!,
            BeforeJson = Truncate(beforeJson, MaxSnapshotLength),
            AfterJson = Truncate(afterJson, MaxSnapshotLength),
            Reason = Truncate(reason, 1000),
            IpAddress = Truncate(ipAddress, 64),
            CorrelationId = Truncate(correlationId, 100)
        };

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
