using FluentAssertions;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class IdempotentDomainEventHandlerTests
{
    private sealed record BookingConfirmedEvent(Guid BookingId) : DomainEvent;

    private sealed class InMemoryInboxStore : IInboxStore
    {
        public HashSet<string> ProcessedKeys { get; } = [];
        public List<(Guid EventId, string HandlerName, string Error)> Failures { get; } = [];

        public Task<bool> HasBeenProcessedAsync(Guid eventId, string handlerName, CancellationToken cancellationToken = default)
        {
            var key = $"{eventId}:{handlerName}";
            return Task.FromResult(ProcessedKeys.Contains(key));
        }

        public Task RecordHandledAsync(Guid eventId, string handlerName, CancellationToken cancellationToken = default)
        {
            var key = $"{eventId}:{handlerName}";
            ProcessedKeys.Add(key);
            return Task.CompletedTask;
        }

        public Task RecordFailedAsync(Guid eventId, string handlerName, string error, CancellationToken cancellationToken = default)
        {
            Failures.Add((eventId, handlerName, error));
            return Task.CompletedTask;
        }
    }

    private sealed class TestBookingConfirmedHandler : IdempotentDomainEventHandler<BookingConfirmedEvent>
    {
        public int ExecutionCount { get; private set; }
        public bool ShouldThrow { get; set; }

        public override string HandlerName => "Notifications.BookingConfirmedHandler";

        public TestBookingConfirmedHandler(IInboxStore inboxStore)
            : base(inboxStore, NullLogger<TestBookingConfirmedHandler>.Instance)
        {
        }

        protected override Task HandleIdempotentAsync(BookingConfirmedEvent domainEvent, CancellationToken cancellationToken)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Simulated notification service failure");
            }

            ExecutionCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task HandleAsync_FirstDelivery_ShouldExecuteAndRecordHandledAsync()
    {
        // Arrange
        var inboxStore = new InMemoryInboxStore();
        var handler = new TestBookingConfirmedHandler(inboxStore);
        var domainEvent = new BookingConfirmedEvent(Guid.NewGuid());

        // Act
        await handler.HandleAsync(domainEvent);

        // Assert
        handler.ExecutionCount.Should().Be(1);
        inboxStore.ProcessedKeys.Should().Contain($"{domainEvent.EventId}:{handler.HandlerName}");
    }

    [Fact]
    public async Task HandleAsync_SecondDelivery_ShouldDeduplicateAndSkipExecutionAsync()
    {
        // Arrange
        var inboxStore = new InMemoryInboxStore();
        var handler = new TestBookingConfirmedHandler(inboxStore);
        var domainEvent = new BookingConfirmedEvent(Guid.NewGuid());

        // First delivery
        await handler.HandleAsync(domainEvent);
        handler.ExecutionCount.Should().Be(1);

        // Act - Second delivery of the identical domain event
        await handler.HandleAsync(domainEvent);

        // Assert - Execution count must still be 1 (idempotent deduplication)
        handler.ExecutionCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_WhenExecutionThrows_ShouldRecordFailureAndRethrowAsync()
    {
        // Arrange
        var inboxStore = new InMemoryInboxStore();
        var handler = new TestBookingConfirmedHandler(inboxStore)
        {
            ShouldThrow = true
        };
        var domainEvent = new BookingConfirmedEvent(Guid.NewGuid());

        // Act
        var act = async () => await handler.HandleAsync(domainEvent);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Simulated notification service failure");

        inboxStore.ProcessedKeys.Should().BeEmpty();
        inboxStore.Failures.Should().HaveCount(1);
        inboxStore.Failures[0].EventId.Should().Be(domainEvent.EventId);
        inboxStore.Failures[0].HandlerName.Should().Be(handler.HandlerName);
        inboxStore.Failures[0].Error.Should().Contain("Simulated notification service failure");
    }
}
