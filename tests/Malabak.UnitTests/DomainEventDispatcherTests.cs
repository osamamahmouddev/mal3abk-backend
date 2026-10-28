using FluentAssertions;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class DomainEventDispatcherTests
{
    private sealed record TestUserCreatedEvent(Guid UserId, string Email) : IDomainEvent;

    private sealed record TestUserRoleChangedEvent(Guid UserId, string NewRole) : IDomainEvent;

    private sealed class TestUserCreatedHandler : IDomainEventHandler<TestUserCreatedEvent>
    {
        public bool Handled { get; private set; }
        public TestUserCreatedEvent? LastEvent { get; private set; }

        public Task HandleAsync(TestUserCreatedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Handled = true;
            LastEvent = domainEvent;
            return Task.CompletedTask;
        }
    }

    private sealed class AnotherUserCreatedHandler : IDomainEventHandler<TestUserCreatedEvent>
    {
        public bool Handled { get; private set; }

        public Task HandleAsync(TestUserCreatedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingEventHandler : IDomainEventHandler<TestUserRoleChangedEvent>
    {
        public Task HandleAsync(TestUserRoleChangedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulation of handler failure");
        }
    }

    [Fact]
    public async Task DispatchAsync_WithRegisteredHandler_ShouldInvokeHandlerSuccessfullyAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        var handler = new TestUserCreatedHandler();
        services.AddSingleton<IDomainEventHandler<TestUserCreatedEvent>>(handler);

        var serviceProvider = services.BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(serviceProvider, NullLogger<DomainEventDispatcher>.Instance);

        var domainEvent = new TestUserCreatedEvent(Guid.NewGuid(), "player@malabak.com");

        // Act
        await dispatcher.DispatchAsync(domainEvent);

        // Assert
        handler.Handled.Should().BeTrue();
        handler.LastEvent.Should().Be(domainEvent);
    }

    [Fact]
    public async Task DispatchAsync_WithMultipleRegisteredHandlers_ShouldInvokeAllHandlersAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        var handler1 = new TestUserCreatedHandler();
        var handler2 = new AnotherUserCreatedHandler();
        services.AddSingleton<IDomainEventHandler<TestUserCreatedEvent>>(handler1);
        services.AddSingleton<IDomainEventHandler<TestUserCreatedEvent>>(handler2);

        var serviceProvider = services.BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(serviceProvider, NullLogger<DomainEventDispatcher>.Instance);

        var domainEvent = new TestUserCreatedEvent(Guid.NewGuid(), "player@malabak.com");

        // Act
        await dispatcher.DispatchAsync(domainEvent);

        // Assert
        handler1.Handled.Should().BeTrue();
        handler2.Handled.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchAsync_WithNoRegisteredHandler_ShouldCompleteWithoutThrowingAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(serviceProvider, NullLogger<DomainEventDispatcher>.Instance);

        var domainEvent = new TestUserCreatedEvent(Guid.NewGuid(), "unhandled@malabak.com");

        // Act
        var act = async () => await dispatcher.DispatchAsync(domainEvent);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerThrows_ShouldUnwrapAndRethrowInnerExceptionAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestUserRoleChangedEvent>>(new FailingEventHandler());

        var serviceProvider = services.BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(serviceProvider, NullLogger<DomainEventDispatcher>.Instance);

        var domainEvent = new TestUserRoleChangedEvent(Guid.NewGuid(), "VenueManager");

        // Act
        var act = async () => await dispatcher.DispatchAsync(domainEvent);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Simulation of handler failure");
    }
}
