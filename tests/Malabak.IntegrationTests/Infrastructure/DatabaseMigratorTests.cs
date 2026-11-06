using System.Reflection;
using FluentAssertions;
using Malabak.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class DatabaseMigratorTests
{
    [Fact]
    public void EveryDatabaseWithMigrations_IsMigratedOnDeploy()
    {
        var migrated = typeof(DatabaseMigrator).Assembly
            .GetReferencedAssemblies()
            .Where(a => a.Name!.StartsWith("Malabak", StringComparison.Ordinal))
            .Select(Assembly.Load)
            .Append(typeof(DatabaseMigrator).Assembly)
            .SelectMany(a => a.GetTypes())
            .Select(t => t.GetCustomAttribute<DbContextAttribute>()?.ContextType)
            .OfType<Type>()
            .Where(t => typeof(DbContext).IsAssignableFrom(t))
            .Distinct()
            .ToList();

        migrated.Should().NotBeEmpty();
        DatabaseMigrator.Contexts.Should().BeEquivalentTo(
            migrated,
            "a module whose migrations are left off this list never gets its tables in a deployed database");
    }
}
