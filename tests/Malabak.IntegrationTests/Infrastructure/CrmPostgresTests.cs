using FluentAssertions;
using Malabak.Modules.Identity.Application.Crm;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>The admin CRM on a real PostgreSQL with the real Identity migrations: search, and one contact per phone number.</summary>
public sealed class CrmPostgresTests : IAsyncLifetime
{
    private readonly string _databaseName = $"crm_{Guid.NewGuid():N}";
    private readonly Guid _admin = Guid.NewGuid();
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await ExecuteAsync(IntegrationTestEnvironment.PostgresConnectionString, $"CREATE DATABASE \"{_databaseName}\"");
        _connectionString = new NpgsqlConnectionStringBuilder(IntegrationTestEnvironment.PostgresConnectionString) { Database = _databaseName }.ConnectionString;
        await using var db = Db();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync(IntegrationTestEnvironment.PostgresConnectionString, $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    [Fact]
    public async Task Search_FindsByNameVenueOrArea_IgnoringCase_AndByExactPhoneAsync()
    {
        await using (var db = Db())
        {
            await Service(db).CreateAsync(new CrmContactInput("Captain Omar", "Nasr Arena", "Nasr City", "01012345678", null, null), _admin);
            await Service(db).CreateAsync(new CrmContactInput("كابتن حسن", "ملعب الشباب", "المعادي", "01098765432", null, null), _admin);
        }

        await using var read = Db();
        (await Service(read).ListAsync(null, "nasr arena", false, 1, 50)).Value.Items.Should().ContainSingle(c => c.Name == "Captain Omar");
        (await Service(read).ListAsync(null, "المعادي", false, 1, 50)).Value.Items.Should().ContainSingle(c => c.Name == "كابتن حسن");
        (await Service(read).ListAsync(null, "+20 109 876 5432", false, 1, 50)).Value.Items.Should().ContainSingle(c => c.Name == "كابتن حسن");
        (await Service(read).ListAsync(CrmStage.Active, null, false, 1, 50)).Value.Total.Should().Be(0);
    }

    [Fact]
    public async Task TwoPeopleAddingTheSameNumberAtOnce_KeepOneContactAsync()
    {
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
        {
            await using var db = Db();
            return await Service(db).CreateAsync(new CrmContactInput($"Owner {i}", null, null, "01155555555", null, null), _admin);
        }));

        attempts.Count(a => a.IsSuccess).Should().Be(1);
        attempts.Single(a => a.IsFailure).Error.Should().Be(CrmErrors.DuplicatePhone);
        await using var verify = Db();
        (await verify.CrmContacts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AStageMoveAndANote_AreSavedOnTheTimelineAsync()
    {
        Guid id;
        await using (var db = Db())
        {
            id = (await Service(db).CreateAsync(new CrmContactInput("Owner", null, null, "01277777777", null, null), _admin)).Value.Contact.Id;
        }

        await using (var db = Db())
        {
            (await Service(db).MoveAsync(id, CrmStage.Interested, null, _admin)).IsSuccess.Should().BeTrue();
        }

        await using (var db = Db())
        {
            (await Service(db).AddNoteAsync(id, CrmNoteKind.Call, "Sending the photos tomorrow", _admin)).IsSuccess.Should().BeTrue();
        }

        await using var read = Db();
        var detail = await Service(read).GetAsync(id);
        detail.Value.Contact.Stage.Should().Be(nameof(CrmStage.Interested));
        detail.Value.Notes.Select(n => n.Kind).Should().BeEquivalentTo(nameof(CrmNoteKind.Call), nameof(CrmNoteKind.StageChange));
    }

    private IdentityDbContext Db() => new(new DbContextOptionsBuilder<IdentityDbContext>()
        .UseNpgsql(_connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema))
        .Options);

    private static CrmService Service(IdentityDbContext db) =>
        new(db, new PhoneNumberNormalizer(), new HashIndex(), new TaggedCipher(), new SystemClock(), new NoAudit(), new FakeUserAccounts());

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NoAudit : IAuditLog
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TaggedCipher : IFieldCipher
    {
        public ValueTask<byte[]> EncryptAsync(string plaintext, FieldEncryptionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(System.Text.Encoding.UTF8.GetBytes($"{context.RowId}:{plaintext}"));

        public ValueTask<string> DecryptAsync(byte[] ciphertext, FieldEncryptionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(System.Text.Encoding.UTF8.GetString(ciphertext).Split(':', 2)[1]);

        public ValueTask<bool> NeedsReEncryptionAsync(byte[] ciphertext, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class HashIndex : IBlindIndexService
    {
        public byte[] Compute(string normalizedValue, string purpose) =>
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(purpose + ":" + normalizedValue));
    }
}
