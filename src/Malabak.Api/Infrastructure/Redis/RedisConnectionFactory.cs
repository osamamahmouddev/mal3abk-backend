using System.Security.Authentication;
using Malabak.Api.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Malabak.Api.Infrastructure.Redis;

/// <summary>
/// Factory for creating and configuring resilient, secure Redis ConnectionMultiplexer instances.
/// </summary>
public static class RedisConnectionFactory
{
    public static ConfigurationOptions CreateConfigurationOptions(RedisOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new ArgumentException("Redis connection string cannot be empty.", nameof(options));
        }

        var configuration = ConfigurationOptions.Parse(options.ConnectionString);

        configuration.ConnectTimeout = options.ConnectTimeoutMs;
        configuration.SyncTimeout = options.SyncTimeoutMs;
        configuration.AbortOnConnectFail = options.AbortOnConnectFail;
        configuration.ClientName = options.ClientName;

        if (options.Ssl.HasValue)
        {
            configuration.Ssl = options.Ssl.Value;
        }

        if (configuration.Ssl)
        {
            configuration.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;

            if (!string.IsNullOrWhiteSpace(options.TlsCaCertificatePath))
            {
                configuration.TrustIssuer(options.TlsCaCertificatePath);
            }
        }

        if (!string.IsNullOrWhiteSpace(options.Password))
        {
            configuration.Password = options.Password;
        }

        if (options.DefaultDatabase.HasValue)
        {
            configuration.DefaultDatabase = options.DefaultDatabase.Value;
        }

        return configuration;
    }

    public static IConnectionMultiplexer CreateConnection(
        RedisOptions options,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var configuration = CreateConfigurationOptions(options);

        var multiplexer = ConnectionMultiplexer.Connect(configuration);

        multiplexer.ConnectionFailed += (_, e) =>
        {
            logger.LogError(
                e.Exception,
                "Redis connection failed on endpoint '{EndPoint}'. FailureType: {FailureType}. ConnectionType: {ConnectionType}.",
                e.EndPoint,
                e.FailureType,
                e.ConnectionType);
        };

        multiplexer.ConnectionRestored += (_, e) =>
        {
            logger.LogInformation(
                "Redis connection restored on endpoint '{EndPoint}'. FailureType: {FailureType}.",
                e.EndPoint,
                e.FailureType);
        };

        multiplexer.ErrorMessage += (_, e) =>
        {
            logger.LogWarning(
                "Redis server returned error on endpoint '{EndPoint}': {Message}",
                e.EndPoint,
                e.Message);
        };

        multiplexer.InternalError += (_, e) =>
        {
            logger.LogError(
                e.Exception,
                "Redis internal client error occurred. Origin: {Origin}",
                e.Origin);
        };

        return multiplexer;
    }
}
