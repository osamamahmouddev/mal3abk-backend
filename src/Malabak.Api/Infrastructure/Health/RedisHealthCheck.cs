using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Malabak.Api.Infrastructure.Health;

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<RedisHealthCheck> _logger;

    public RedisHealthCheck(IConnectionMultiplexer multiplexer, ILogger<RedisHealthCheck> logger)
    {
        _multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_multiplexer.IsConnected)
        {
            return HealthCheckResult.Unhealthy("Redis connection multiplexer is disconnected.");
        }

        try
        {
            var database = _multiplexer.GetDatabase();
            var latency = await database.PingAsync().ConfigureAwait(false);

            var data = new Dictionary<string, object>
            {
                ["latencyMs"] = latency.TotalMilliseconds,
                ["endpoints"] = string.Join(", ", _multiplexer.GetEndPoints().Select(e => e.ToString() ?? "unknown"))
            };

            if (latency.TotalMilliseconds > 500)
            {
                return HealthCheckResult.Degraded(
                    $"Redis is responding with high latency: {latency.TotalMilliseconds:F1}ms",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"Redis is healthy. Latency: {latency.TotalMilliseconds:F1}ms",
                data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis health check failed");
            return HealthCheckResult.Unhealthy("Redis health check failed with an error.", ex);
        }
    }
}
