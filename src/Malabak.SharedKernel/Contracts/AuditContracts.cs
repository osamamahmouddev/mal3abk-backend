namespace Malabak.SharedKernel.Contracts;

/// <summary>Who performed an audited action.</summary>
public static class AuditActorType
{
    public const string User = "User";
    public const string Admin = "Admin";
    public const string System = "System";
}

/// <summary>
/// One sensitive action to record. <paramref name="Before"/>/<paramref name="After"/> are snapshots of the entity (any
/// object; serialized to JSON with sensitive fields masked). <paramref name="EntityId"/> is a string so composite or
/// external ids fit.
/// </summary>
public sealed record AuditEntry(
    string Action,
    string EntityType,
    string EntityId,
    Guid? ActorId,
    string ActorType,
    object? Before = null,
    object? After = null,
    string? Reason = null);

/// <summary>Append-only audit trail (TASK-062). Recording never fails the caller's business operation.</summary>
public interface IAuditLog
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>Request facts added to every audit entry (supplied by the host: client IP, impersonator, correlation id).</summary>
public interface IAuditRequestContext
{
    string? IpAddress { get; }

    /// <summary>Set while an admin acts through an impersonation token: the admin's user id.</summary>
    Guid? ImpersonatorId { get; }

    string? CorrelationId { get; }
}
