using Testcontainers.PostgreSql;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure.Containers;

public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;

    public PostgreSqlContainerFixture()
    {
        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("malabak_test_db")
            .WithUsername("malabak_test_user")
            .WithPassword("malabak_test_pass")
            .Build();
    }

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
