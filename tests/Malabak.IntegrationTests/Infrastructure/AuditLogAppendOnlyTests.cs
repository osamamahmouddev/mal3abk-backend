using FluentAssertions;
using Malabak.Modules.Audit.Domain;
using Malabak.Modules.Audit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>TASK-062: the real migration makes audit rows impossible to change or remove, even for the app's own connection.</summary>
public sealed class AuditLogAppendOnlyTests : IAsyncLifetime
{
    private readonly string _databaseName = $"audit_append_only_{Guid.NewGuid():N}";
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var admin = IntegrationTestEnvironment.PostgresConnectionString;
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        _connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = _databaseName }.ConnectionString;
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(IntegrationTestEnvironment.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Rows_CanBeInserted_ButNeverUpdatedDeletedOrTruncatedAsync()
    {
        await using var context = CreateContext();
        var row = AuditLogEntry.Create(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "Admin", null, "venue.suspended", "Venue", "v-1",
            null, "{\"status\":\"Suspended\"}", "test", "127.0.0.1", "trace");
        context.Entries.Add(row);
        await context.SaveChangesAsync();

        var update = () => context.Database.ExecuteSqlRawAsync("UPDATE audit.audit_logs SET \"Reason\" = 'rewritten'");
        var delete = () => context.Database.ExecuteSqlRawAsync("DELETE FROM audit.audit_logs");
        var truncate = () => context.Database.ExecuteSqlRawAsync("TRUNCATE audit.audit_logs");

        (await update.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await truncate.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);

        await using var check = CreateContext();
        (await check.Entries.SingleAsync()).Reason.Should().Be("test");
    }

    private AuditDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(_connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AuditDbContext.Schema))
            .Options);
}
