namespace Malabak.SharedKernel.Caching;

/// <summary>
/// Service for building consistently formatted, validated Redis keys for the current environment.
/// </summary>
public interface IRedisKeyBuilder
{
    string Environment { get; }
    string Prefix { get; }

    /// <summary>
    /// Formats a Redis key using the standard convention: {prefix}:{environment}:{module}:{category}:{identifiers...}
    /// </summary>
    string Format(string module, string category, params string[] identifiers);

    /// <summary>
    /// Generates key for an OTP verification code: malabak:{env}:identity:otp:{purpose}:{phone}
    /// </summary>
    string Otp(string phone, string purpose);

    /// <summary>
    /// Generates key for a single-use WebSocket ticket: malabak:{env}:identity:ws-ticket:{jti}
    /// </summary>
    string WsTicket(string jti);

    /// <summary>
    /// Generates key for a single-use support impersonation code: malabak:{env}:identity:impersonation:{code}
    /// </summary>
    string Impersonation(string code);

    /// <summary>
    /// Generates resource key for distributed slot lock: malabak:{env}:booking:lock:slot:{pitchId}:{slotStartUtc:yyyyMMddHHmm}
    /// </summary>
    string SlotLock(Guid pitchId, DateTime slotStartUtc);

    /// <summary>
    /// Generates key for rate limiter counter: malabak:{env}:ratelimit:{category}:{identifier}
    /// </summary>
    string RateLimit(string category, string identifier);
}
