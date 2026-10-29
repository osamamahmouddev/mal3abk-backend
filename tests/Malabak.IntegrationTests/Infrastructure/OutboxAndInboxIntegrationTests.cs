using FluentAssertions;
using Malabak.Api.Infrastructure.Configuration;
using Malabak.Api.Infrastructure.Outbox;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Events;
using Malabak.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed record AggregateCreatedIntegrationEvent(Guid AggregateId, string Name) : DomainEvent;

public sealed class TestAggregate : Entity<Guid>
{
    public string Name { get; private set; } = string.Empty;

    public static TestAggregate Create(string name)
    {
        var aggregate = new TestAggregate
        {
            Id = IdGenerator.NewGuid(),
            Name = name
        };
        aggregate.RaiseDomainEvent(new AggregateCreatedIntegrationEvent(aggregate.Id, name));
        return aggregate;
    }
}

public sealed class IntegrationTestDbContext : BaseDbContext
{
    public const string Schema = "outbox_it";
    public override string ModuleSchema => Schema;

    public DbSet<TestAggregate> Aggregates => Set<TestAggregate>();

    public IntegrationTestDbContext(DbContextOptions<IntegrationTestDbContext> options, IClock clock)
        : base(options, clock)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<TestAggregate>(builder =>
        {
            builder.ToTable("test_aggregates");
            builder.HasKey(a => a.Id);
            builder.Property(a => a.Name).HasMaxLength(100).IsRequired();
        });
    }
}

public sealed class CountingIntegrationEventHandler : IDomainEventHandler<AggregateCreatedIntegrationEvent>
{
    public int ExecutionCount { get; private set; }
    public AggregateCreatedIntegrationEvent? LastEvent { get; private set; }

    public Task HandleAsync(AggregateCreatedIntegrationEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ExecutionCount++;
        LastEvent = domainEvent;
        return Task.CompletedTask;
    }
}

public sealed class FailingIntegrationEventHandler : IDomainEventHandler<AggregateCreatedIntegrationEvent>
{
    public Task HandleAsync(AggregateCreatedIntegrationEvent domainEvent, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Integration test simulated handler failure");
    }
}

public sealed class IdempotentAuditIntegrationConsumer : IdempotentDomainEventHandler<AggregateCreatedIntegrationEvent>
{
    public int ExecutionCount { get; private set; }
    public override string HandlerName => "Audit.AggregateCreatedConsumer";

    public IdempotentAuditIntegrationConsumer(IInboxStore inboxStore)
        : base(inboxStore, NullLogger<IdempotentAuditIntegrationConsumer>.Instance)
    {
    }

    protected override Task HandleIdempotentAsync(AggregateCreatedIntegrationEvent domainEvent, CancellationToken cancellationToken)
    {
        ExecutionCount++;
        return Task.CompletedTask;
    }
}

public sealed class OutboxAndInboxIntegrationTests
{
    private static string ConnectionString => IntegrationTestEnvironment.PostgresConnectionString;
    private readonly IClock _clock = new SystemClock();
    private static bool _initialized;
    private static readonly object _initLock = new();

    private IntegrationTestDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<IntegrationTestDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        var context = new IntegrationTestDbContext(options, _clock);

        if (!_initialized)
        {
            lock (_initLock)
            {
                if (!_initialized)
                {
                    context.Database.ExecuteSqlRaw($"CREATE SCHEMA IF NOT EXISTS {IntegrationTestDbContext.Schema};");
                    var creator = (RelationalDatabaseCreator)context.Database.GetService<IDatabaseCreator>();
                    try
                    {
                        creator.CreateTables();
                    }
                    catch (PostgresException ex) when (ex.SqlState == "42P07")
                    {
                        // Table already exists
                    }
                    _initialized = true;
                }
            }
        }

        return context;
    }

    private async Task<IntegrationTestDbContext> CreateAndResetDbContextAsync()
    {
        var context = CreateDbContext();
        await context.Database.ExecuteSqlRawAsync($"TRUNCATE TABLE {IntegrationTestDbContext.Schema}.outbox_messages, {IntegrationTestDbContext.Schema}.inbox_messages, {IntegrationTestDbContext.Schema}.test_aggregates CASCADE;");
        return context;
    }

    [Fact]
    public async Task OutboxInterceptor_WhenAggregateSaved_PersistsEntityAndOutboxMessageInSameTransactionAsync()
    {
        // Arrange
        await using var context = await CreateAndResetDbContextAsync();
        var aggregate = TestAggregate.Create("Venue-AlAhly");

        // Act
        context.Aggregates.Add(aggregate);
        await context.SaveChangesAsync();

        // Assert
        var savedAggregate = await context.Aggregates.FirstOrDefaultAsync(a => a.Id == aggregate.Id);
        savedAggregate.Should().NotBeNull();
        savedAggregate!.Name.Should().Be("Venue-AlAhly");

        var outboxMessage = await context.OutboxMessages.FirstOrDefaultAsync(m => m.Content.Contains("Venue-AlAhly"));
        outboxMessage.Should().NotBeNull();
        outboxMessage!.ProcessedOnUtc.Should().BeNull();
        outboxMessage.Content.Should().Contain("Venue-AlAhly");
        outboxMessage.Type.Should().Contain(nameof(AggregateCreatedIntegrationEvent));
    }

    [Fact]
    public async Task OutboxInterceptor_WhenTransactionRolledBack_DoesNotPersistOutboxMessageAsync()
    {
        // Arrange
        await using var context = await CreateAndResetDbContextAsync();
        var aggregate = TestAggregate.Create("RolledBackVenue");

        // Act
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            context.Aggregates.Add(aggregate);
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        // Assert
        await using var freshContext = CreateDbContext();
        var savedAggregate = await freshContext.Aggregates.FirstOrDefaultAsync(a => a.Id == aggregate.Id);
        savedAggregate.Should().BeNull();

        var outboxMessage = await freshContext.OutboxMessages.FirstOrDefaultAsync(m => m.Content.Contains("RolledBackVenue"));
        outboxMessage.Should().BeNull();
    }

    [Fact]
    public async Task OutboxDispatcher_PendingMessages_DispatchesAndMarksAsProcessedAsync()
    {
        // Arrange
        await using var context = await CreateAndResetDbContextAsync();
        var aggregate = TestAggregate.Create("DispatchedVenue");
        context.Aggregates.Add(aggregate);
        await context.SaveChangesAsync();

        var handler = new CountingIntegrationEventHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<AggregateCreatedIntegrationEvent>>(handler);
        var serviceProvider = services.BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(serviceProvider, NullLogger<DomainEventDispatcher>.Instance);

        var options = Options.Create(new OutboxOptions { BatchSize = 10, MaxRetries = 3 });
        var source = new OutboxMessageSource<IntegrationTestDbContext>(
            context,
            dispatcher,
            _clock,
            options,
            NullLogger<OutboxMessageSource<IntegrationTestDbContext>>.Instance);

        // Act
        var dispatchedCount = await source.DispatchPendingMessagesAsync(batchSize: 10);

        // Assert
        dispatchedCount.Should().BeGreaterThan(0);
        handler.ExecutionCount.Should().Be(1);
        handler.LastEvent.Should().NotBeNull();
        handler.LastEvent!.Name.Should().Be("DispatchedVenue");

        var updatedMessage = await context.OutboxMessages.FirstAsync(m => m.Content.Contains("DispatchedVenue"));
        updatedMessage.ProcessedOnUtc.Should().NotBeNull();
        updatedMessage.Error.Should().BeNull();
    }

    [Fact]
    public async Task OutboxDispatcher_WhenHandlerThrows_RetriesWithExponentialBackoffAsync()
    {
        // Arrange
        await using var context = await CreateAndResetDbContextAsync();
        var aggregate = TestAggregate.Create("FailingVenue");
        context.Aggregates.Add(aggregate);
        await context.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<AggregateCreatedIntegrationEvent>>(new FailingIntegrationEventHandler());
        var serviceProvider = services.BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(serviceProvider, NullLogger<DomainEventDispatcher>.Instance);

        var options = Options.Create(new OutboxOptions { BatchSize = 10, MaxRetries = 3 });
        var source = new OutboxMessageSource<IntegrationTestDbContext>(
            context,
            dispatcher,
            _clock,
            options,
            NullLogger<OutboxMessageSource<IntegrationTestDbContext>>.Instance);

        // Act
        var dispatchedCount = await source.DispatchPendingMessagesAsync(batchSize: 10);

        // Assert
        dispatchedCount.Should().Be(0);

        var updatedMessage = await context.OutboxMessages.FirstAsync(m => m.Content.Contains("FailingVenue"));
        updatedMessage.ProcessedOnUtc.Should().BeNull();
        updatedMessage.RetryCount.Should().Be(1);
        updatedMessage.NextRetryUtc.Should().NotBeNull();
        updatedMessage.NextRetryUtc.Should().BeAfter(_clock.UtcNow);
        updatedMessage.Error.Should().Contain("Integration test simulated handler failure");
    }

    [Fact]
    public async Task InboxDeduplication_WhenEventDeliveredTwice_ExecutesConsumerOnlyOnceAsync()
    {
        // Arrange
        await using var context = await CreateAndResetDbContextAsync();
        var inboxStore = new EfInboxStore<IntegrationTestDbContext>(context, _clock);
        var consumer = new IdempotentAuditIntegrationConsumer(inboxStore);
        var domainEvent = new AggregateCreatedIntegrationEvent(Guid.NewGuid(), "Zamalek-Court");

        // Act: First delivery
        await consumer.HandleAsync(domainEvent);

        // Assert first execution
        consumer.ExecutionCount.Should().Be(1);
        var inboxRecord = await context.InboxMessages
            .FirstOrDefaultAsync(m => m.EventId == domainEvent.EventId && m.HandlerName == consumer.HandlerName);
        inboxRecord.Should().NotBeNull();
        inboxRecord!.ProcessedOnUtc.Should().NotBeNull();

        // Act: Second delivery (duplicate / retry delivery)
        await consumer.HandleAsync(domainEvent);

        // Assert: execution count remains exactly 1 (deduplication guarantee)
        consumer.ExecutionCount.Should().Be(1);
    }
}
