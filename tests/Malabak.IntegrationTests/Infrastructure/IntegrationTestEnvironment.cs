using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// Provides the backing services for every integration test run.
/// <para>
/// By default it starts throwaway PostgreSQL 17 (with btree_gist) and Redis 7.2 containers with
/// random credentials, so tests never depend on credentials committed to the repo (see SECURITY.md)
/// and run the same on a laptop and in CI. To reuse services that are already running (for example
/// <c>docker compose up</c>), set <c>MALABAK_TEST_POSTGRES</c> and <c>MALABAK_TEST_REDIS</c>.
/// </para>
/// The API reads the resulting connection strings from environment variables when each
/// <c>WebApplicationFactory</c> boots. Containers are removed by the Testcontainers resource reaper
/// when the test process exits.
/// </summary>
internal static class IntegrationTestEnvironment
{
    /// <summary>PostgreSQL connection string for tests that talk to the database directly.</summary>
    public static string PostgresConnectionString => Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")!;

    /// <summary>Redis connection string for tests that talk to Redis directly.</summary>
    public static string RedisConnectionString => Environment.GetEnvironmentVariable("ConnectionStrings__Redis")!;

    private const string _postgresImage = "postgres:17-alpine";
    private const string _redisImage = "redis:7.2-alpine";
    private const int _redisPort = 6379;

    // Cloudflare's published Turnstile test keys (always pass) — not secrets.
    private const string _turnstileTestSecretKey = "1x0000000000000000000000000000000AA";
    private const string _turnstileTestSiteKey = "1x00000000000000000000AA";

#pragma warning disable CA2255 // Module initializers are intended for this: one-time setup of the test assembly.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        var postgres = Environment.GetEnvironmentVariable("MALABAK_TEST_POSTGRES");
        var redis = Environment.GetEnvironmentVariable("MALABAK_TEST_REDIS");

        if (string.IsNullOrWhiteSpace(postgres) || string.IsNullOrWhiteSpace(redis))
        {
            (postgres, redis) = StartContainers();
        }

        Set("ConnectionStrings__Postgres", postgres);
        Set("Database__ConnectionString", postgres);
        Set("ConnectionStrings__Redis", redis);
        Set("Redis__ConnectionString", redis);
        Set("Security__Turnstile__SecretKey", _turnstileTestSecretKey);
        Set("Security__Turnstile__SiteKey", _turnstileTestSiteKey);
    }

    /// <remarks>
    /// Runs inside the module initializer, while the runtime holds this assembly's initialization lock.
    /// Any code from this assembly scheduled on another thread (an <c>async</c> method continuation,
    /// <c>Task.Run</c>) would block on that lock and deadlock. So this method stays synchronous and
    /// only blocks on Testcontainers' own tasks, with no synchronization context to post back to.
    /// </remarks>
    private static (string Postgres, string Redis) StartContainers()
    {
        var redisPassword = RandomSecret();

        var postgresContainer = new PostgreSqlBuilder()
            .WithImage(_postgresImage)
            .WithDatabase("malabak_test")
            .WithUsername("malabak_test")
            .WithPassword(RandomSecret())
            .Build();

        var redisContainer = new ContainerBuilder()
            .WithImage(_redisImage)
            .WithCommand("redis-server", "--requirepass", redisPassword)
            .WithPortBinding(_redisPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(_redisPort))
            .Build();

        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            Task.WhenAll(postgresContainer.StartAsync(), redisContainer.StartAsync()).GetAwaiter().GetResult();

            // Booking overlap protection relies on the GiST exclusion constraint.
            var extension = postgresContainer.ExecScriptAsync("CREATE EXTENSION IF NOT EXISTS btree_gist;").GetAwaiter().GetResult();
            if (extension.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to enable btree_gist in the test database: {extension.Stderr}");
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        var redisConnection =
            $"{redisContainer.Hostname}:{redisContainer.GetMappedPublicPort(_redisPort)},password={redisPassword},abortConnect=false";

        return (postgresContainer.GetConnectionString(), redisConnection);
    }

    private static string RandomSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    private static void Set(string name, string value) => Environment.SetEnvironmentVariable(name, value);
}
