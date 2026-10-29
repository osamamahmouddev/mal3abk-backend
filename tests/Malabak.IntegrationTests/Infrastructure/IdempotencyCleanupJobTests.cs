using FluentAssertions;
using Malabak.Api.Infrastructure.Idempotency;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class IdempotencyCleanupJobTests
{
    private sealed class FakeIdempotencyStore : IIdempotencyStore
    {
        public int PurgedCount { get; set; } = 5;
        public DateTime? LastCutoffUtc { get; private set; }

        public Task<IdempotencyRecord?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<IdempotencyRecord?>(null);

        public Task CreatePendingAsync(IdempotencyRecord record, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CompleteAsync(string key, int statusCode, string? responseContentType, string? responseBody, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> CleanupExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
        {
            LastCutoffUtc = cutoffUtc;
            return Task.FromResult(PurgedCount);
        }
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCallStoreCleanupWithClockUtcNowAsync()
    {
        // Arrange
        var now = new DateTime(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FixedClock(now);
        var store = new FakeIdempotencyStore();
        var job = new IdempotencyCleanupJob(store, clock, NullLogger<IdempotencyCleanupJob>.Instance);

        // Act
        await job.ExecuteAsync(CancellationToken.None);

        // Assert
        store.LastCutoffUtc.Should().Be(now);
    }
}
