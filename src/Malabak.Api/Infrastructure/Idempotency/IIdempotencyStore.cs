using Malabak.SharedKernel.Persistence;

namespace Malabak.Api.Infrastructure.Idempotency;

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task CreatePendingAsync(IdempotencyRecord record, CancellationToken cancellationToken = default);
    Task CompleteAsync(string key, int statusCode, string? responseContentType, string? responseBody, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
    Task<int> CleanupExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
}
