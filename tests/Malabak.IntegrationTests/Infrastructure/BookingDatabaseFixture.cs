using Malabak.Modules.Booking.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// Creates a throwaway database inside the test PostgreSQL container and applies the real Booking migrations to it,
/// so the tests below exercise the actual <c>no_overlapping_active_bookings</c> exclusion constraint.
/// </summary>
public sealed class BookingDatabaseFixture : IAsyncLifetime
{
    private readonly string _databaseName = $"booking_overlap_{Guid.NewGuid():N}";

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var admin = IntegrationTestEnvironment.PostgresConnectionString;
        await ExecuteAsync(admin, $"CREATE DATABASE \"{_databaseName}\"");

        ConnectionString = new NpgsqlConnectionStringBuilder(admin) { Database = _databaseName, Pooling = true }.ConnectionString;

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync(IntegrationTestEnvironment.PostgresConnectionString, $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    /// <summary>A context on the migrated database. Pass a clock to switch on the interceptors, including the outbox.</summary>
    public BookingDbContext CreateContext(Malabak.SharedKernel.IClock? clock = null)
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", BookingDbContext.Schema))
            .Options;
        return new BookingDbContext(options, clock);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
