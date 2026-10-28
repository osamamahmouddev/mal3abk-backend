using Malabak.Api.Infrastructure.Configuration;
using Malabak.SharedKernel.Caching;
using Malabak.SharedKernel.Locking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Malabak.Api.Infrastructure.Redis;

public static class RedisServiceCollectionExtensions
{
    public static IServiceCollection AddRedisInfrastructure(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        // Register Redis Key Conventions builder
        services.AddSingleton<IRedisKeyBuilder>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            var envName = options.EnvironmentName ?? environment.EnvironmentName;
            var prefix = string.IsNullOrWhiteSpace(options.KeyPrefix) ? RedisKeyConventions.DefaultPrefix : options.KeyPrefix;
            return new RedisKeyConventions(envName, prefix);
        });

        // Register Singleton ConnectionMultiplexer
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Malabak.Infrastructure.Redis");
            return RedisConnectionFactory.CreateConnection(options, logger);
        });

        // Register Distributed Locking
        services.AddSingleton<IDistributedLockProvider, RedisDistributedLockProvider>();

        // Register Atomic Single-Use Token Store
        services.AddSingleton<IAtomicConsumeStore, RedisAtomicConsumeStore>();

        return services;
    }
}
