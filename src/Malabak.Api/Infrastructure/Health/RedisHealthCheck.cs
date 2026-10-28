using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Malabak.Api.Infrastructure.Health;

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<RedisHealthCheck> _logger;

    public RedisHealthCheck(IConnectionMultiplexer multiplexer, ILogger<RedisHealthCheck> logger)
    {
        _multiplexer = multiplexer;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var database = _multiplexer.GetDatabase();
            var latency = await database.PingAsync();

            return HealthCheckResult.Healthy($"Redis is healthy. Latency: {latency.TotalMilliseconds:F1}ms");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis health check failed");
            return HealthCheckResult.Unhealthy("Redis connection failed.", ex);
        }
    }
}
