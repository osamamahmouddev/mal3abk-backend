using FluentAssertions;
using Malabak.Api.Infrastructure.Hangfire;
using Malabak.SharedKernel.Caching;
using Malabak.SharedKernel.Locking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class IdempotentBackgroundJobTests
{
    private readonly FakeDistributedLockProvider _lockProvider = new();
    private readonly FakeAtomicConsumeStore _atomicStore = new();

    [Fact]
    public async Task ExecuteAsync_WhenFirstRun_ShouldExecuteJobCoreAndMarkCompletedAsync()
    {
        // Arrange
        var job = new TestIdempotentJob(_lockProvider, _atomicStore);
        var key = "payment-capture-1001";
        var payload = new TestJobPayload("order-555");

        // Act
        await job.ExecuteAsync(key, payload);

        // Assert
        job.ExecutionCount.Should().Be(1);
        var isCompleted = await _atomicStore.ExistsAsync($"malabak:jobs:completed:{key}");
        isCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WhenAlreadyCompleted_ShouldSkipExecutionAsync()
    {
        // Arrange
        var job = new TestIdempotentJob(_lockProvider, _atomicStore);
        var key = "booking-confirm-2002";
        var payload = new TestJobPayload("slot-888");

        // Pre-mark as completed
        await _atomicStore.SetAsync($"malabak:jobs:completed:{key}", "completed", TimeSpan.FromDays(1));

        // Act
        await job.ExecuteAsync(key, payload);

        // Assert: must NOT execute again
        job.ExecutionCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenLockCannotBeAcquired_ShouldYieldWithoutExecutingAsync()
    {
        // Arrange
        _lockProvider.ShouldAcquire = false; // Simulate active lock by another worker
        var job = new TestIdempotentJob(_lockProvider, _atomicStore);
        var key = "sms-dispatch-3003";
        var payload = new TestJobPayload("phone-123");

        // Act
        await job.ExecuteAsync(key, payload);

        // Assert: must NOT execute without lock
        job.ExecutionCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_WithNullOrWhitespaceKey_ShouldThrowArgumentExceptionAsync(string? invalidKey)
    {
        // Arrange
        var job = new TestIdempotentJob(_lockProvider, _atomicStore);
        var payload = new TestJobPayload("test");

        // Act
        var act = async () => await job.ExecuteAsync(invalidKey!, payload);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    public sealed record TestJobPayload(string ReferenceId);

    private sealed class TestIdempotentJob : IdempotentBackgroundJob<TestJobPayload>
    {
        public int ExecutionCount { get; private set; }

        public TestIdempotentJob(IDistributedLockProvider lockProvider, IAtomicConsumeStore atomicStore)
            : base(lockProvider, atomicStore, NullLogger<TestIdempotentJob>.Instance)
        {
        }

        protected override Task ExecuteCoreAsync(TestJobPayload args, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDistributedLockProvider : IDistributedLockProvider
    {
        public bool ShouldAcquire { get; set; } = true;

        public Task<IDistributedLock?> TryAcquireLockAsync(
            string resource,
            TimeSpan expiry,
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default)
        {
            if (!ShouldAcquire)
            {
                return Task.FromResult<IDistributedLock?>(null);
            }

            var @lock = (IDistributedLock)new FakeDistributedLock(resource);
            return Task.FromResult<IDistributedLock?>(@lock);
        }

        public Task<IDistributedLock> AcquireLockAsync(
            string resource,
            TimeSpan expiry,
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IDistributedLock>(new FakeDistributedLock(resource));
        }
    }

    private sealed class FakeDistributedLock : IDistributedLock
    {
        public string Resource { get; }
        public string LockToken { get; } = Guid.NewGuid().ToString("N");
        public bool IsAcquired { get; private set; } = true;
        public DateTime AcquiredAtUtc { get; } = DateTime.UtcNow;
        public TimeSpan Expiry { get; } = TimeSpan.FromMinutes(5);

        public FakeDistributedLock(string resource) => Resource = resource;

        public Task<bool> ReleaseAsync(CancellationToken cancellationToken = default)
        {
            IsAcquired = false;
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync()
        {
            IsAcquired = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAtomicConsumeStore : IAtomicConsumeStore
    {
        private readonly Dictionary<string, string> _store = [];

        public Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task SetJsonAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            _store[key] = System.Text.Json.JsonSerializer.Serialize(value);
            return Task.CompletedTask;
        }

        public Task<string?> ConsumeAsync(string key, CancellationToken cancellationToken = default)
        {
            if (_store.Remove(key, out var val))
            {
                return Task.FromResult<string?>(val);
            }
            return Task.FromResult<string?>(null);
        }

        public Task<T?> ConsumeJsonAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            var raw = ConsumeAsync(key, cancellationToken).Result;
            if (raw is null) return Task.FromResult<T?>(default);
            return Task.FromResult(System.Text.Json.JsonSerializer.Deserialize<T>(raw));
        }

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_store.ContainsKey(key));
        }

        public Task<bool> InvalidateAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_store.Remove(key));
        }
    }
}
