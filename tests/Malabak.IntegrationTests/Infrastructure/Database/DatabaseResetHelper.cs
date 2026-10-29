using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Malabak.IntegrationTests.Infrastructure.Database;

public static class DatabaseResetHelper
{
    private static readonly string[] _applicationSchemas =
    [
        "identity",
        "venues",
        "booking",
        "audit",
        "notifications",
        "payments",
        "subscriptions",
        "support",
        "teams",
        "matches"
    ];

    public static async Task ResetPostgresDatabaseAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var schemasList = string.Join(", ", _applicationSchemas.Select(s => $"'{s}'"));

        var query = $@"
            SELECT table_schema, table_name 
            FROM information_schema.tables 
            WHERE table_schema IN ({schemasList}) 
              AND table_type = 'BASE TABLE' 
              AND table_name != '__EFMigrationsHistory';";

        var tablesToTruncate = new List<string>();

        await using (var command = new NpgsqlCommand(query, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var schema = reader.GetString(0);
                var table = reader.GetString(1);
                tablesToTruncate.Add($"\"{schema}\".\"{table}\"");
            }
        }

        if (tablesToTruncate.Count > 0)
        {
            var truncateSql = $"TRUNCATE TABLE {string.Join(", ", tablesToTruncate)} RESTART IDENTITY CASCADE;";
            await using var truncateCommand = new NpgsqlCommand(truncateSql, connection);
            await truncateCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public static async Task ResetDbContextAsync<TContext>(TContext context, CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Database.IsNpgsql())
        {
            var connectionString = context.Database.GetConnectionString();
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                await ResetPostgresDatabaseAsync(connectionString, cancellationToken);
            }
        }
        else
        {
            // In-Memory or fallback
            await context.Database.EnsureDeletedAsync(cancellationToken);
            await context.Database.EnsureCreatedAsync(cancellationToken);
        }
    }
}
