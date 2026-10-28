namespace Malabak.SharedKernel.Caching;

/// <summary>
/// Store for short-lived, single-use security tokens (OTP codes, WebSocket tickets, impersonation codes).
/// Guarantees atomic get-and-delete: a token can only be consumed once, preventing replay attacks.
/// </summary>
public interface IAtomicConsumeStore
{
    /// <summary>
    /// Stores a value under <paramref name="key"/> with a given expiration TTL.
    /// </summary>
    Task SetAsync(
        string key,
        string value,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a JSON-serialized object under <paramref name="key"/> with a given expiration TTL.
    /// </summary>
    Task SetJsonAsync<T>(
        string key,
        T value,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically retrieves and deletes the value associated with <paramref name="key"/>.
    /// Returns null if the key does not exist or has already been consumed.
    /// </summary>
    Task<string?> ConsumeAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically retrieves, deserializes, and deletes the JSON object associated with <paramref name="key"/>.
    /// Returns default(T) if the key does not exist or has already been consumed.
    /// </summary>
    Task<T?> ConsumeJsonAsync<T>(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the specified key currently exists without consuming it.
    /// </summary>
    Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the key immediately (e.g. on revocation or cancellation).
    /// </summary>
    Task<bool> InvalidateAsync(
        string key,
        CancellationToken cancellationToken = default);
}
