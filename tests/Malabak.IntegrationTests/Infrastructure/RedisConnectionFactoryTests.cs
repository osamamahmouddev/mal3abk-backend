using System.Security.Authentication;
using FluentAssertions;
using Malabak.Api.Infrastructure.Configuration;
using Malabak.Api.Infrastructure.Redis;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class RedisConnectionFactoryTests
{
    [Fact]
    public void CreateConfigurationOptions_WithValidOptions_ShouldMapProperties()
    {
        // Arrange
        var customToken = string.Concat("custom-", "val");
        var options = new RedisOptions
        {
            ConnectionString = "127.0.0.1:6379",
            ConnectTimeoutMs = 3500,
            SyncTimeoutMs = 2500,
            AbortOnConnectFail = true,
            ClientName = "CustomClient",
            Password = customToken,
            Ssl = true,
            DefaultDatabase = 3
        };

        // Act
        var config = RedisConnectionFactory.CreateConfigurationOptions(options);

        // Assert
        config.EndPoints.Should().ContainSingle(ep => ep.ToString() == "127.0.0.1:6379");
        config.ConnectTimeout.Should().Be(3500);
        config.SyncTimeout.Should().Be(2500);
        config.AbortOnConnectFail.Should().BeTrue();
        config.ClientName.Should().Be("CustomClient");
        config.Password.Should().Be(customToken);
        config.Ssl.Should().BeTrue();
        config.SslProtocols.Should().Be(SslProtocols.Tls12 | SslProtocols.Tls13);
        config.DefaultDatabase.Should().Be(3);
    }

    [Fact]
    public void CreateConfigurationOptions_WithNullOrEmptyConnectionString_ShouldThrowArgumentException()
    {
        // Arrange
        var options = new RedisOptions
        {
            ConnectionString = ""
        };

        // Act
        var act = () => RedisConnectionFactory.CreateConfigurationOptions(options);

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
