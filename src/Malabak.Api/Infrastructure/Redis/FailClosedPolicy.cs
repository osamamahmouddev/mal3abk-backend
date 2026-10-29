using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Malabak.Api.Infrastructure.Redis;

/// <summary>
/// Policy orchestrator implementing fail-closed and fail-open behaviors for Redis operations.
/// </summary>
public static class FailClosedPolicy
{
    /// <summary>
    /// Executes a sensitive Redis operation enforcing Fail-Closed semantics.
    /// If Redis is unreachable, times out, or throws an error, a <see cref="RedisFailClosedException"/> is thrown
    /// to prevent security or consistency bypass.
    /// </summary>
    public static async Task<T> ExecuteSensitiveAsync<T>(
        string operation,
        string? resourceKey,
        Func<Task<T>> action,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(logger);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            logger.LogError(
                ex,
                "Fail-closed policy triggered for sensitive operation '{Operation}' on resource '{Resource}'. Redis command failed. Request will be rejected.",
                operation,
                SanitizeKeyForLogging(resourceKey));

            throw new RedisFailClosedException(
                operation,
                resourceKey,
                $"Sensitive operation '{operation}' failed because Redis is unavailable or returned an error.",
                ex);
        }
        catch (TimeoutException ex)
        {
            logger.LogError(
                ex,
                "Fail-closed policy triggered for sensitive operation '{Operation}' on resource '{Resource}'. Redis timed out. Request will be rejected.",
                operation,
                SanitizeKeyForLogging(resourceKey));

            throw new RedisFailClosedException(
                operation,
                resourceKey,
                $"Sensitive operation '{operation}' timed out against Redis.",
                ex);
        }
    }

    /// <summary>
    /// Executes a non-sensitive Redis operation (e.g. read-through cache) with Fail-Open semantics.
    /// If Redis fails, a warning is logged and the <paramref name="fallbackValue"/> is returned.
    /// </summary>
    public static async Task<T> ExecuteResilientAsync<T>(
        string operation,
        string? resourceKey,
        Func<Task<T>> action,
        T fallbackValue,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(logger);

        if (cancellationToken.IsCancellationRequested)
        {
            return fallbackValue;
        }

        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(
                ex,
                "Fail-open policy: Non-sensitive cache operation '{Operation}' for resource '{Resource}' failed. Falling back to default provider.",
                operation,
                SanitizeKeyForLogging(resourceKey));

            return fallbackValue;
        }
    }

    private static string SanitizeKeyForLogging(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "(unspecified)";
        }

        // Mask potential phone numbers or codes in keys e.g. otp, code
        if (key.Contains(":otp:", StringComparison.OrdinalIgnoreCase) ||
            key.Contains(":impersonation:", StringComparison.OrdinalIgnoreCase))
        {
            var segments = key.Split(':');
            if (segments.Length > 0)
            {
                segments[^1] = "***MASKED***";
                return string.Join(':', segments);
            }
        }

        return key;
    }
}
