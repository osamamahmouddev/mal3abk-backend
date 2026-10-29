using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure.Containers;

public sealed class RedisContainerFixture : IAsyncLifetime
{
    private const int _redisPort = 6379;
    private readonly IContainer _container;

    public RedisContainerFixture()
    {
        _container = new ContainerBuilder()
            .WithImage("redis:7-alpine")
            .WithPortBinding(_redisPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(_redisPort))
            .Build();
    }

    public string ConnectionString =>
        $"{_container.Hostname}:{_container.GetMappedPublicPort(_redisPort)},abortConnect=false";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
