using Malabak.Modules.Venues.Application.Common;
using Malabak.SharedKernel.Contracts;

namespace Malabak.Modules.Venues.Infrastructure.Services;

/// <summary>Venues-module audit adapter: forwards to the platform's append-only audit trail (TASK-062).</summary>
public sealed class LoggingVenueAuditLogger : IVenueAuditLogger
{
    private readonly IAuditLog _audit;

    public LoggingVenueAuditLogger(IAuditLog audit)
    {
        _audit = audit;
    }

    public Task LogAsync(
        string action,
        string entityName,
        Guid entityId,
        Guid userId,
        object? metadata = null,
        CancellationToken cancellationToken = default) =>
        _audit.RecordAsync(new AuditEntry(action, entityName, entityId.ToString("D"), userId, AuditActorType.User, After: metadata), cancellationToken);
}
