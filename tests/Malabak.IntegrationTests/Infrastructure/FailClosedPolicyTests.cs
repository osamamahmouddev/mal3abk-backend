using FluentAssertions;
using Malabak.Api.Infrastructure.Redis;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class FailClosedPolicyTests
{
    private readonly NullLogger<FailClosedPolicyTests> _logger = NullLogger<FailClosedPolicyTests>.Instance;

    [Fact]
    public async Task ExecuteSensitiveAsync_WhenSuccessful_ShouldReturnResultAsync()
    {
        // Act
        var result = await FailClosedPolicy.ExecuteSensitiveAsync(
            "TestOperation",
            "malabak:test:key",
            () => Task.FromResult(42),
            _logger);

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteSensitiveAsync_WhenRedisExceptionOccurs_ShouldThrowRedisFailClosedExceptionAsync()
    {
        // Act
        var act = async () => await FailClosedPolicy.ExecuteSensitiveAsync<string>(
            "AcquireSlotLock",
            "malabak:dev:booking:lock:slot:pitch1",
            () => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated connection drop"),
            _logger);

        // Assert
        var ex = await act.Should().ThrowAsync<RedisFailClosedException>();
        ex.Which.Operation.Should().Be("AcquireSlotLock");
        ex.Which.ResourceKey.Should().Be("malabak:dev:booking:lock:slot:pitch1");
        ex.Which.InnerException.Should().BeOfType<RedisConnectionException>();
    }

    [Fact]
    public async Task ExecuteSensitiveAsync_WhenRedisTimeoutExceptionOccurs_ShouldThrowRedisFailClosedExceptionAsync()
    {
        // Act
        var act = async () => await FailClosedPolicy.ExecuteSensitiveAsync<bool>(
            "VerifyOtp",
            "malabak:dev:identity:otp:login:+201012345678",
            () => throw new RedisTimeoutException("Simulated Redis timeout", CommandStatus.Unknown),
            _logger);

        // Assert
        var ex = await act.Should().ThrowAsync<RedisFailClosedException>();
        ex.Which.Operation.Should().Be("VerifyOtp");
        ex.Which.InnerException.Should().BeOfType<RedisTimeoutException>();
    }

    [Fact]
    public async Task ExecuteSensitiveAsync_WhenGenericTimeoutExceptionOccurs_ShouldThrowRedisFailClosedExceptionAsync()
    {
        // Act
        var act = async () => await FailClosedPolicy.ExecuteSensitiveAsync<bool>(
            "ConsumeWsTicket",
            "malabak:dev:identity:ws-ticket:jti-99",
            () => throw new TimeoutException("General network timeout"),
            _logger);

        // Assert
        var ex = await act.Should().ThrowAsync<RedisFailClosedException>();
        ex.Which.Operation.Should().Be("ConsumeWsTicket");
        ex.Which.InnerException.Should().BeOfType<TimeoutException>();
    }

    [Fact]
    public async Task ExecuteResilientAsync_WhenSuccessful_ShouldReturnResultAsync()
    {
        // Act
        var result = await FailClosedPolicy.ExecuteResilientAsync(
            "GetCachedPitchDetails",
            "malabak:dev:venues:pitch:10",
            () => Task.FromResult("CachedPitchData"),
            fallbackValue: "FallbackDbData",
            _logger);

        // Assert
        result.Should().Be("CachedPitchData");
    }

    [Fact]
    public async Task ExecuteResilientAsync_WhenRedisFails_ShouldFailOpenAndReturnFallbackAsync()
    {
        // Act
        var result = await FailClosedPolicy.ExecuteResilientAsync<string>(
            "GetCachedPitchDetails",
            "malabak:dev:venues:pitch:10",
            () => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "Redis unavailable"),
            fallbackValue: "FallbackDbData",
            _logger);

        // Assert: must NOT throw, instead returns fallback data
        result.Should().Be("FallbackDbData");
    }
}
