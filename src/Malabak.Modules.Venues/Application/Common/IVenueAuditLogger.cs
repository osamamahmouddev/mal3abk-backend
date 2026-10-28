namespace Malabak.Modules.Venues.Application.Common;

public interface IVenueAuditLogger
{
    Task LogAsync(string action, string entityName, Guid entityId, Guid userId, object? metadata = null, CancellationToken cancellationToken = default);
}
