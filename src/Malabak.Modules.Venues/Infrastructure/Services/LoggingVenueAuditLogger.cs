using System.Text.Json;
using Malabak.Modules.Venues.Application.Common;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Venues.Infrastructure.Services;

public sealed class LoggingVenueAuditLogger : IVenueAuditLogger
{
    private readonly ILogger<LoggingVenueAuditLogger> _logger;

    public LoggingVenueAuditLogger(ILogger<LoggingVenueAuditLogger> logger)
    {
        _logger = logger;
    }

    public Task LogAsync(
        string action,
        string entityName,
        Guid entityId,
        Guid userId,
        object? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var metaJson = metadata != null ? JsonSerializer.Serialize(metadata) : null;
        _logger.LogInformation(
            "AUDIT: [{Action}] Entity: {EntityName}({EntityId}) By User: {UserId}. Metadata: {Metadata}",
            action,
            entityName,
            entityId,
            userId,
            metaJson);

        return Task.CompletedTask;
    }
}
