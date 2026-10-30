namespace Malabak.SharedKernel.Jobs;

/// <summary>
/// Contract for background jobs requiring idempotent execution guarantees.
/// Prevents duplicate processing when jobs are retried or redelivered.
/// </summary>
public interface IIdempotentJob<in TArgs>
{
    /// <summary>
    /// Executes the background job with the given idempotency key.
    /// </summary>
    Task ExecuteAsync(string idempotencyKey, TArgs args, CancellationToken cancellationToken = default);
}
