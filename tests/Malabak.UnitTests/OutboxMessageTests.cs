using FluentAssertions;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Persistence;
using Xunit;

namespace Malabak.UnitTests;

public sealed class OutboxMessageTests
{
    [Fact]
    public void MarkAsProcessed_ShouldSetTimestampAndClearErrorAndLocks()
    {
        // Arrange
        var message = new OutboxMessage
        {
            Type = "TestEvent",
            Content = "{}"
        };
        message.MarkAsFailed("Temporary error", DateTime.UtcNow.AddMinutes(1));
        var processedAt = DateTime.UtcNow;

        // Act
        message.MarkAsProcessed(processedAt);

        // Assert
        message.ProcessedOnUtc.Should().Be(processedAt);
        message.Error.Should().BeNull();
        message.LockToken.Should().BeNull();
        message.LockedUntilUtc.Should().BeNull();
    }

    [Fact]
    public void MarkAsFailed_ShouldIncrementRetryCountAndSetNextRetry()
    {
        // Arrange
        var message = new OutboxMessage
        {
            Type = "TestEvent",
            Content = "{}"
        };
        var nextRetry = DateTime.UtcNow.AddSeconds(10);

        // Act
        message.MarkAsFailed("Database connection timeout", nextRetry);

        // Assert
        message.RetryCount.Should().Be(1);
        message.Error.Should().Be("Database connection timeout");
        message.NextRetryUtc.Should().Be(nextRetry);
        message.ProcessedOnUtc.Should().BeNull();
    }

    [Fact]
    public void TryAcquireLock_WhenUnlocked_ShouldAcquireSuccessfully()
    {
        // Arrange
        var message = new OutboxMessage();
        var now = DateTime.UtcNow;
        var lockedUntil = now.AddMinutes(1);

        // Act
        var acquired = message.TryAcquireLock("worker-1", lockedUntil, now);

        // Assert
        acquired.Should().BeTrue();
        message.LockToken.Should().Be("worker-1");
        message.LockedUntilUtc.Should().Be(lockedUntil);
    }

    [Fact]
    public void TryAcquireLock_WhenActiveLockExists_ShouldRejectSecondAcquisition()
    {
        // Arrange
        var message = new OutboxMessage();
        var now = DateTime.UtcNow;
        var lockedUntil = now.AddMinutes(1);
        message.TryAcquireLock("worker-1", lockedUntil, now);

        // Act
        var secondAcquired = message.TryAcquireLock("worker-2", now.AddMinutes(2), now);

        // Assert
        secondAcquired.Should().BeFalse();
        message.LockToken.Should().Be("worker-1");
    }

    [Fact]
    public void TryAcquireLock_WhenPreviousLockExpired_ShouldAllowTakeover()
    {
        // Arrange
        var message = new OutboxMessage();
        var originalLockTime = DateTime.UtcNow.AddMinutes(-5);
        message.TryAcquireLock("worker-1", originalLockTime, originalLockTime.AddMinutes(-1));

        var now = DateTime.UtcNow;
        var newLockExpiry = now.AddMinutes(1);

        // Act
        var acquired = message.TryAcquireLock("worker-2", newLockExpiry, now);

        // Assert
        acquired.Should().BeTrue();
        message.LockToken.Should().Be("worker-2");
        message.LockedUntilUtc.Should().Be(newLockExpiry);
    }
}
