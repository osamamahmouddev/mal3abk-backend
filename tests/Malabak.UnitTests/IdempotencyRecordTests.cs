using FluentAssertions;
using Malabak.SharedKernel.Persistence;
using Xunit;

namespace Malabak.UnitTests;

public sealed class IdempotencyRecordTests
{
    [Fact]
    public void IdempotencyRecord_InitialState_ShouldBePending()
    {
        // Arrange & Act
        var record = new IdempotencyRecord
        {
            Key = "key-123",
            RequestHash = "hash-123",
            HttpMethod = "POST",
            RequestPath = "/api/v1/bookings",
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(24)
        };

        // Assert
        record.Status.Should().Be(IdempotencyStatus.Pending);
        record.StatusCode.Should().Be(0);
        record.ResponseBody.Should().BeNull();
        record.ResponseContentType.Should().BeNull();
    }

    [Fact]
    public void MarkAsCompleted_ShouldUpdateStateAndResponseAttributes()
    {
        // Arrange
        var record = new IdempotencyRecord
        {
            Key = "key-123",
            RequestHash = "hash-123",
            HttpMethod = "POST",
            RequestPath = "/api/v1/bookings"
        };

        // Act
        record.MarkAsCompleted(201, "application/json", "{\"id\":\"booking-999\"}");

        // Assert
        record.Status.Should().Be(IdempotencyStatus.Completed);
        record.StatusCode.Should().Be(201);
        record.ResponseContentType.Should().Be("application/json");
        record.ResponseBody.Should().Be("{\"id\":\"booking-999\"}");
    }

    [Fact]
    public void MarkAsFailed_ShouldSetStatusToFailed()
    {
        // Arrange
        var record = new IdempotencyRecord
        {
            Key = "key-123",
            RequestHash = "hash-123"
        };

        // Act
        record.MarkAsFailed();

        // Assert
        record.Status.Should().Be(IdempotencyStatus.Failed);
    }

    [Fact]
    public void IsExpired_WhenCurrentUtcPastExpiresAt_ShouldReturnTrue()
    {
        // Arrange
        var createdAt = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        var record = new IdempotencyRecord
        {
            CreatedAtUtc = createdAt,
            ExpiresAtUtc = createdAt.AddHours(24)
        };

        // Act & Assert
        record.IsExpired(createdAt.AddHours(23)).Should().BeFalse();
        record.IsExpired(createdAt.AddHours(24)).Should().BeTrue();
        record.IsExpired(createdAt.AddHours(25)).Should().BeTrue();
    }
}
