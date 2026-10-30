using FluentAssertions;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class PersistenceTests
{
    private sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class TestEntity : Entity<Guid>
    {
        public string Name { get; set; } = string.Empty;

        public static TestEntity Create(string name)
        {
            return new TestEntity
            {
                Id = IdGenerator.NewGuid(),
                Name = name
            };
        }
    }

    private sealed class TestDbContext : BaseDbContext
    {
        public override string ModuleSchema => "test";
        public DbSet<TestEntity> TestEntities => Set<TestEntity>();

        public TestDbContext(DbContextOptions<TestDbContext> options, IClock clock)
            : base(options, clock)
        {
        }
    }

    [Fact]
    public void IdGenerator_NewGuid_ShouldGenerateValidGuidV7()
    {
        // Act
        var id1 = IdGenerator.NewGuid();
        var id2 = IdGenerator.NewGuid();

        // Assert
        id1.Should().NotBe(Guid.Empty);
        id2.Should().NotBe(Guid.Empty);
        id1.Should().NotBe(id2);

        // Verify version 7 nibble (7 in byte 7 / version bits)
        var bytes = id1.ToByteArray();
        // In RFC 9562 big-endian representation, the version is in the high 4 bits of the 7th byte
        // In .NET little-endian representation, version is in bytes[7] >> 4
        var version = (bytes[7] >> 4) & 0x0F;
        version.Should().Be(7, "Guid.CreateVersion7 must produce version 7 UUID");
    }

    [Fact]
    public async Task AuditableEntityInterceptor_OnAdd_ShouldSetCreatedAtUtcAsync()
    {
        // Arrange
        var clock = new TestClock();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new TestDbContext(options, clock);
        var entity = TestEntity.Create("Test Team");

        // Act
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        // Assert
        entity.CreatedAtUtc.Should().Be(clock.UtcNow);
        entity.UpdatedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task AuditableEntityInterceptor_OnModify_ShouldSetUpdatedAtUtcAsync()
    {
        // Arrange
        var clock = new TestClock();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new TestDbContext(options, clock);
        var entity = TestEntity.Create("Initial Name");
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        // Advance clock by 2 hours
        clock.UtcNow = clock.UtcNow.AddHours(2);

        // Act
        entity.Name = "Updated Name";
        context.Entry(entity).State = EntityState.Modified;
        await context.SaveChangesAsync();

        // Assert
        entity.UpdatedAtUtc.Should().Be(clock.UtcNow);
        entity.UpdatedAtUtc.Should().BeAfter(entity.CreatedAtUtc);
    }

    [Fact]
    public async Task SoftDeleteInterceptor_OnRemove_ShouldMarkIsDeletedAndExcludeFromQueriesAsync()
    {
        // Arrange
        var clock = new TestClock();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new TestDbContext(options, clock);
        var entity = TestEntity.Create("Entity to Soft Delete");
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        // Act - Remove entity via DbContext
        context.TestEntities.Remove(entity);
        await context.SaveChangesAsync();

        // Assert entity state
        entity.IsDeleted.Should().BeTrue();
        entity.DeletedAtUtc.Should().Be(clock.UtcNow);

        // Assert default query filter excludes soft-deleted entity
        var activeEntities = await context.TestEntities.ToListAsync();
        activeEntities.Should().BeEmpty();

        // Assert IgnoreQueryFilters includes soft-deleted entity
        var allEntities = await context.TestEntities.IgnoreQueryFilters().ToListAsync();
        allEntities.Should().ContainSingle(e => e.Id == entity.Id && e.IsDeleted);
    }
}
