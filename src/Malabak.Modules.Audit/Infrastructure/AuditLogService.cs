using Malabak.Modules.Audit.Application;
using Malabak.Modules.Audit.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Audit.Infrastructure;

/// <summary>
/// Writes audit entries. Uses its own DbContext so an entry is stored even when the caller's unit of work is rolled back
/// afterwards; a failure to write is logged loudly but never fails the business operation that was being audited.
/// </summary>
public sealed class AuditLogService : IAuditLog
{
    private readonly AuditDbContext _dbContext;
    private readonly IAuditRequestContext _request;
    private readonly IClock _clock;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(AuditDbContext dbContext, IAuditRequestContext request, IClock clock, ILogger<AuditLogService> logger)
    {
        _dbContext = dbContext;
        _request = request;
        _clock = clock;
        _logger = logger;
    }

    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        try
        {
            var row = AuditLogEntry.Create(
                IdGenerator.NewGuid(), _clock.UtcNow, entry.ActorId, entry.ActorType, _request.ImpersonatorId, entry.Action, entry.EntityType,
                entry.EntityId, AuditMasking.Serialize(entry.Before), AuditMasking.Serialize(entry.After), entry.Reason, _request.IpAddress,
                _request.CorrelationId);
            _dbContext.Entries.Add(row);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _dbContext.Entry(row).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "AUDIT WRITE FAILED: {Action} on {EntityType}({EntityId}) by {ActorId}", entry.Action, entry.EntityType, entry.EntityId, entry.ActorId);
        }
    }
}
