namespace Malabak.Api.Infrastructure.Redis;

/// <summary>
/// Thrown when a sensitive operation fails due to Redis unavailability, network errors, or timeouts.
/// Adheres to the "Fail-Secure" (Fail-Closed) directive in SECURITY.md §1 & §7:
/// If Redis fails, sensitive operations (slot locking, OTP verification, WS ticket consumption,
/// impersonation validation) must reject access instead of silently bypassing security controls.
/// </summary>
public sealed class RedisFailClosedException : Exception
{
    public string Operation { get; }
    public string? ResourceKey { get; }

    public RedisFailClosedException(string operation, string? resourceKey, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
        ResourceKey = resourceKey;
    }
}
