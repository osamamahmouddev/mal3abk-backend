using FluentAssertions;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class OutboxInterceptorTests
{
    private sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 10, 28, 14, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.Utc;
    }

    private sealed record UserRegisteredDomainEvent(Guid UserId, string FullName) : IDomainEvent;

    private sealed class AggregateTestEntity : Entity<Guid>
    {
        public string Name { get; set; } = string.Empty;

        public static AggregateTestEntity Create(string name)
        {
            var entity = new AggregateTestEntity
            {
                Id = IdGenerator.NewGuid(),
                Name = name
            };

            entity.RaiseDomainEvent(new UserRegisteredDomainEvent(entity.Id, name));
            return entity;
        }

        public static AggregateTestEntity CreateWithoutEvents(string name)
        {
            return new AggregateTestEntity
            {
                Id = IdGenerator.NewGuid(),
                Name = name
            };
        }
    }

    private sealed class OutboxTestDbContext : BaseDbContext
    {
        public override string ModuleSchema => "test";
        public DbSet<AggregateTestEntity> AggregateEntities => Set<AggregateTestEntity>();

        public OutboxTestDbContext(DbContextOptions<OutboxTestDbContext> options, IClock clock)
            : base(options, clock)
        {
        }
    }

    [Fact]
    public async Task SaveChangesAsync_WithDomainEvents_ShouldCreateOutboxMessagesAndClearEventsAsync()
    {
        // Arrange
        var clock = new TestClock();
        var options = new DbContextOptionsBuilder<OutboxTestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new OutboxTestDbContext(options, clock);
        var aggregate = AggregateTestEntity.Create("Ahmed Ali");
        aggregate.DomainEvents.Should().HaveCount(1);

        context.AggregateEntities.Add(aggregate);

        // Act
        var result = await context.SaveChangesAsync();

        // Assert
        result.Should().BeGreaterThan(0);
        aggregate.DomainEvents.Should().BeEmpty(); // Events must be cleared from the entity

        var outboxMessages = await context.OutboxMessages.ToListAsync();
        outboxMessages.Should().HaveCount(1);

        var message = outboxMessages[0];
        message.Id.Should().NotBeEmpty();
        message.Type.Should().Contain(nameof(UserRegisteredDomainEvent));
        message.Content.Should().Contain("Ahmed Ali");
        message.ProcessedOnUtc.Should().BeNull();
        message.OccurredOnUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SaveChangesAsync_WithoutDomainEvents_ShouldNotCreateOutboxMessagesAsync()
    {
        // Arrange
        var clock = new TestClock();
        var options = new DbContextOptionsBuilder<OutboxTestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new OutboxTestDbContext(options, clock);
        var aggregate = AggregateTestEntity.CreateWithoutEvents("No Event Entity");

        context.AggregateEntities.Add(aggregate);

        // Act
        await context.SaveChangesAsync();

        // Assert
        var outboxMessages = await context.OutboxMessages.ToListAsync();
        outboxMessages.Should().BeEmpty();
    }
}
