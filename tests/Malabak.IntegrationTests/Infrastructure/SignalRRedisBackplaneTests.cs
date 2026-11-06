using FluentAssertions;
using Malabak.Api.Infrastructure.Realtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using SignalRRedisOptions = Microsoft.AspNetCore.SignalR.StackExchangeRedis.RedisOptions;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>The SignalR backplane must connect to Redis exactly like the app does (TLS, CA, password, timeouts).</summary>
public sealed class SignalRRedisBackplaneTests
{
    [Fact]
    public void AddRealtimeInfrastructure_BuildsBackplaneFromTheAppRedisOptions()
    {
        var redisToken = string.Concat("backplane-", "test");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:ConnectionString"] = "127.0.0.1:6379,ssl=true",
                ["Redis:Password"] = redisToken,
                ["Redis:ConnectTimeoutMs"] = "1234",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddRealtimeInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        var backplane = provider.GetRequiredService<IOptions<SignalRRedisOptions>>().Value.Configuration;
        backplane.Ssl.Should().BeTrue();
        backplane.Password.Should().Be(redisToken);
        backplane.ConnectTimeout.Should().Be(1234);
        backplane.ChannelPrefix.ToString().Should().Be("malabak:signalr:");
    }
}
