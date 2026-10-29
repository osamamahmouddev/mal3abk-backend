using Malabak.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Api.Infrastructure.Idempotency;

public class EfIdempotencyStore<TDbContext> : IIdempotencyStore
    where TDbContext : BaseDbContext
{
    private readonly TDbContext _dbContext;

    public EfIdempotencyStore(TDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IdempotencyRecord?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == key, cancellationToken);
    }

    public async Task CreatePendingAsync(IdempotencyRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        _dbContext.IdempotencyRecords.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(
        string key,
        int statusCode,
        string? responseContentType,
        string? responseBody,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var existing = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == key, cancellationToken);

        if (existing is not null)
        {
            existing.MarkAsCompleted(statusCode, responseContentType, responseBody);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var existing = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == key, cancellationToken);

        if (existing is not null)
        {
            _dbContext.IdempotencyRecords.Remove(existing);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> CleanupExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        return await _dbContext.IdempotencyRecords
            .Where(r => r.ExpiresAtUtc <= cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
