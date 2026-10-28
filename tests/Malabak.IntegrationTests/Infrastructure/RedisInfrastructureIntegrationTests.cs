using FluentAssertions;
using Malabak.Api.Infrastructure.Configuration;
using Malabak.Api.Infrastructure.Health;
using Malabak.Api.Infrastructure.Redis;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Caching;
using Malabak.SharedKernel.Locking;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

[Collection("RedisIntegration")]
public sealed class RedisInfrastructureIntegrationTests : IAsyncLifetime
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly RedisDistributedLockProvider _lockProvider;
    private readonly RedisAtomicConsumeStore _atomicStore;
    private readonly RedisHealthCheck _healthCheck;
    private readonly RedisKeyConventions _keyBuilder;

    public RedisInfrastructureIntegrationTests()
    {
        var options = new RedisOptions
        {
            ConnectionString = "localhost:6379,password=malabak_dev_redis_password,abortConnect=false",
            ConnectTimeoutMs = 5000,
            SyncTimeoutMs = 5000,
            AbortOnConnectFail = false,
            ClientName = "MalabakIntegrationTests"
        };

        _multiplexer = RedisConnectionFactory.CreateConnection(options, NullLogger.Instance);
        var clock = new SystemClock();

        _lockProvider = new RedisDistributedLockProvider(
            _multiplexer,
            clock,
            NullLogger<RedisDistributedLockProvider>.Instance);

        _atomicStore = new RedisAtomicConsumeStore(
            _multiplexer,
            NullLogger<RedisAtomicConsumeStore>.Instance);

        _healthCheck = new RedisHealthCheck(
            _multiplexer,
            NullLogger<RedisHealthCheck>.Instance);

        _keyBuilder = new RedisKeyConventions("integration-test", "malabak");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _multiplexer.DisposeAsync();
    }

    [Fact]
    public async Task RedisHealthCheck_WhenRedisIsRunning_ShouldReturnHealthyWithLatencyAsync()
    {
        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("Redis is healthy");
        result.Data.Should().ContainKey("latencyMs");
        result.Data.Should().ContainKey("endpoints");
    }

    [Fact]
    public async Task DistributedLock_WhenAcquired_ShouldPreventConcurrentAcquisitionUntilReleasedAsync()
    {
        // Arrange
        var resource = _keyBuilder.SlotLock(Guid.NewGuid(), DateTime.UtcNow);

        // Act 1: First node acquires lock
        var lock1 = await _lockProvider.TryAcquireLockAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            timeout: TimeSpan.Zero);

        lock1.Should().NotBeNull();
        lock1!.IsAcquired.Should().BeTrue();
        lock1.Resource.Should().Be(resource);
        lock1.LockToken.Should().NotBeNullOrWhiteSpace();

        // Act 2: Second node attempts to acquire the same lock with zero timeout -> must fail
        var lock2 = await _lockProvider.TryAcquireLockAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            timeout: TimeSpan.Zero);

        lock2.Should().BeNull();

        // Act 3: First node releases lock
        var released = await lock1.ReleaseAsync();
        released.Should().BeTrue();
        lock1.IsAcquired.Should().BeFalse();

        // Act 4: Second node can now acquire the lock
        var lock3 = await _lockProvider.TryAcquireLockAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            timeout: TimeSpan.Zero);

        lock3.Should().NotBeNull();
        lock3!.IsAcquired.Should().BeTrue();

        // Cleanup
        await lock3.DisposeAsync();
    }

    [Fact]
    public async Task DistributedLock_DisposeAsync_ShouldSafelyReleaseLockAsync()
    {
        // Arrange
        var resource = _keyBuilder.Format("booking", "lock", Guid.NewGuid().ToString("N"));

        // Act & Assert
        await using (var @lock = await _lockProvider.AcquireLockAsync(
            resource,
            expiry: TimeSpan.FromSeconds(15),
            timeout: TimeSpan.FromMilliseconds(500)))
        {
            @lock.IsAcquired.Should().BeTrue();
        }

        // After using block, lock should be free
        var subsequentLock = await _lockProvider.TryAcquireLockAsync(
            resource,
            expiry: TimeSpan.FromSeconds(15),
            timeout: TimeSpan.Zero);

        subsequentLock.Should().NotBeNull();
        await subsequentLock!.DisposeAsync();
    }

    [Fact]
    public async Task AtomicConsume_SetAndConsume_ShouldReturnStoredValueOnceAndThenNullAsync()
    {
        // Arrange
        var key = _keyBuilder.WsTicket(Guid.NewGuid().ToString("N"));
        var ticketPayload = "user-session-claims-xyz";

        // Act 1: Store single-use token
        await _atomicStore.SetAsync(key, ticketPayload, TimeSpan.FromMinutes(1));

        var existsBefore = await _atomicStore.ExistsAsync(key);
        existsBefore.Should().BeTrue();

        // Act 2: Consume for the first time
        var firstConsume = await _atomicStore.ConsumeAsync(key);
        firstConsume.Should().Be(ticketPayload);

        // Act 3: Consume again (replay attempt) -> must return null
        var secondConsume = await _atomicStore.ConsumeAsync(key);
        secondConsume.Should().BeNull();

        var existsAfter = await _atomicStore.ExistsAsync(key);
        existsAfter.Should().BeFalse();
    }

    [Fact]
    public async Task AtomicConsume_JsonPayload_ShouldSerializeAndDeserializeAtomicallyAsync()
    {
        // Arrange
        var key = _keyBuilder.Otp("+201112223344", "login");
        var otpInfo = new OtpVerificationRecord("123456", "login", 0, DateTime.UtcNow);

        // Act 1: Store JSON object
        await _atomicStore.SetJsonAsync(key, otpInfo, TimeSpan.FromMinutes(5));

        // Act 2: Consume JSON object
        var consumed = await _atomicStore.ConsumeJsonAsync<OtpVerificationRecord>(key);

        // Assert
        consumed.Should().NotBeNull();
        consumed!.Code.Should().Be("123456");
        consumed.Purpose.Should().Be("login");

        // Act 3: Subsequent consume returns null
        var secondConsumed = await _atomicStore.ConsumeJsonAsync<OtpVerificationRecord>(key);
        secondConsumed.Should().BeNull();
    }

    [Fact]
    public async Task AtomicConsume_InvalidateAsync_ShouldImmediatelyRemoveKeyAsync()
    {
        // Arrange
        var key = _keyBuilder.Impersonation(Guid.NewGuid().ToString("N"));
        await _atomicStore.SetAsync(key, "impersonation-token", TimeSpan.FromMinutes(2));

        // Act
        var invalidated = await _atomicStore.InvalidateAsync(key);

        // Assert
        invalidated.Should().BeTrue();
        var exists = await _atomicStore.ExistsAsync(key);
        exists.Should().BeFalse();
    }

    public sealed record OtpVerificationRecord(string Code, string Purpose, int Attempts, DateTime CreatedAtUtc);
}
