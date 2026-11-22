using FluentAssertions;
using Malabak.Api.Infrastructure.Platform;
using Malabak.IntegrationTests.Infrastructure.Builders;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Notifications.Domain;
using Malabak.Modules.Notifications.Infrastructure;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Persistence;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// The nightly data retention on a real PostgreSQL with the real migrations: only technical rows past their retention are
/// removed, and anything still in use (a live session, a message not delivered yet, a pending push) is kept.
/// </summary>
public sealed class DataRetentionTests : IAsyncLifetime
{
    private static readonly DateTime _nowUtc = new(2026, 11, 22, 3, 15, 0, DateTimeKind.Utc);
    private readonly string _databaseName = $"retention_{Guid.NewGuid():N}";
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await ExecuteAsync(IntegrationTestEnvironment.PostgresConnectionString, $"CREATE DATABASE \"{_databaseName}\"");
        _connectionString = new NpgsqlConnectionStringBuilder(IntegrationTestEnvironment.PostgresConnectionString) { Database = _databaseName }.ConnectionString;
        await using var identity = Identity();
        await identity.Database.MigrateAsync();
        await using var notifications = Notifications();
        await notifications.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync(IntegrationTestEnvironment.PostgresConnectionString, $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    [Fact]
    public async Task RefreshTokens_OnlyThoseExpiredMoreThan30DaysAgoAreDeletedAsync()
    {
        var user = await SeedUserAsync();
        await using (var db = Identity())
        {
            db.RefreshTokens.Add(Token(user, "old", _nowUtc.AddDays(-31)));
            db.RefreshTokens.Add(Token(user, "recently-expired", _nowUtc.AddDays(-5)));
            db.RefreshTokens.Add(Token(user, "live", _nowUtc.AddDays(3)));
            await db.SaveChangesAsync();
        }

        await using (var db = Identity())
        {
            (await new RefreshTokenRetention(db).PurgeAsync(_nowUtc)).Should().Be(1);
        }

        await using var check = Identity();
        (await check.RefreshTokens.IgnoreQueryFilters().Select(t => t.TokenHash).ToListAsync())
            .Should().BeEquivalentTo("recently-expired", "live");
    }

    [Fact]
    public async Task Sessions_UnusedFor90DaysAreDeleted_UnlessTheyStillHoldALiveRefreshTokenAsync()
    {
        var user = await SeedUserAsync();
        var dead = UserSession.Create(Guid.NewGuid(), user, "127.0.0.1", "test", _nowUtc.AddDays(-120));
        var stillValid = UserSession.Create(Guid.NewGuid(), user, "127.0.0.1", "test", _nowUtc.AddDays(-120));
        var recent = UserSession.Create(Guid.NewGuid(), user, "127.0.0.1", "test", _nowUtc.AddDays(-10));
        await using (var db = Identity())
        {
            db.UserSessions.AddRange(dead, stillValid, recent);
            db.RefreshTokens.Add(Token(user, "valid", _nowUtc.AddDays(2), stillValid.Id));
            await db.SaveChangesAsync();
        }

        await using (var db = Identity())
        {
            (await new InactiveSessionRetention(db).PurgeAsync(_nowUtc)).Should().Be(1);
        }

        await using var check = Identity();
        (await check.UserSessions.IgnoreQueryFilters().Select(s => s.Id).ToListAsync())
            .Should().BeEquivalentTo(new[] { stillValid.Id, recent.Id });
    }

    [Fact]
    public async Task SmsLogs_Older_Than90DaysAreDeletedAsync()
    {
        await using (var db = Identity())
        {
            db.SmsDispatches.Add(SmsDispatch.Create("+20***1", "hash-1", "otp", "sms", _nowUtc.AddDays(-91)));
            db.SmsDispatches.Add(SmsDispatch.Create("+20***2", "hash-2", "otp", "sms", _nowUtc.AddDays(-20)));
            await db.SaveChangesAsync();
        }

        await using (var db = Identity())
        {
            (await new SmsDispatchRetention(db).PurgeAsync(_nowUtc)).Should().Be(1);
        }

        await using var check = Identity();
        (await check.SmsDispatches.Select(d => d.PhoneHash).ToListAsync()).Should().Equal("hash-2");
    }

    [Fact]
    public async Task Outbox_DeliveredMessagesGoAfterAWeek_UndeliveredOnesStay_InboxAfter30DaysAsync()
    {
        await using (var db = Identity())
        {
            db.OutboxMessages.Add(Outbox("delivered-long-ago", _nowUtc.AddDays(-8)));
            db.OutboxMessages.Add(Outbox("delivered-yesterday", _nowUtc.AddDays(-1)));
            db.OutboxMessages.Add(Outbox("never-delivered", null));
            db.InboxMessages.Add(Inbox("old-handler", _nowUtc.AddDays(-31)));
            db.InboxMessages.Add(Inbox("recent-handler", _nowUtc.AddDays(-8)));
            await db.SaveChangesAsync();
        }

        await using (var db = Identity())
        {
            (await new OutboxRetention<IdentityDbContext>(db).PurgeAsync(_nowUtc)).Should().Be(2);
        }

        await using var check = Identity();
        (await check.OutboxMessages.Select(m => m.Type).ToListAsync()).Should().BeEquivalentTo("delivered-yesterday", "never-delivered");
        (await check.InboxMessages.Select(m => m.HandlerName).ToListAsync()).Should().Equal("recent-handler");
    }

    [Fact]
    public async Task Notifications_ProcessedOutboxAndFinishedPushesAreDeleted_PendingPushesStayAsync()
    {
        var user = Guid.NewGuid();
        await using (var db = Notifications())
        {
            var processed = NotificationOutboxMessage.Create(user, "booking.confirmed", new Dictionary<string, string>(), "k-old", _nowUtc.AddDays(-10));
            processed.MarkProcessed(_nowUtc.AddDays(-10));
            var waiting = NotificationOutboxMessage.Create(user, "booking.confirmed", new Dictionary<string, string>(), "k-new", _nowUtc.AddDays(-10));
            db.Outbox.AddRange(processed, waiting);

            var notification = Notification.Create(user, "booking.confirmed", NotificationCategory.Bookings, new Dictionary<string, string>(), null, "n-1", _nowUtc.AddDays(-40));
            var phone = DeviceToken.Create(user, Guid.NewGuid(), "phone-token", DevicePlatform.Android, "ar", _nowUtc.AddDays(-40));
            var tablet = DeviceToken.Create(user, Guid.NewGuid(), "tablet-token", DevicePlatform.Ios, "ar", _nowUtc.AddDays(-40));
            db.Notifications.Add(notification);
            db.DeviceTokens.AddRange(phone, tablet);
            var sent = PushDelivery.Create(notification.Id, phone.Id, _nowUtc.AddDays(-40));
            sent.MarkSent();
            var pending = PushDelivery.Create(notification.Id, tablet.Id, _nowUtc.AddDays(-40));
            db.PushDeliveries.AddRange(sent, pending);
            await db.SaveChangesAsync();
        }

        await using (var db = Notifications())
        {
            (await new NotificationOutboxRetention(db).PurgeAsync(_nowUtc)).Should().Be(1);
            (await new PushDeliveryRetention(db).PurgeAsync(_nowUtc)).Should().Be(1);
        }

        await using var check = Notifications();
        (await check.Outbox.Select(m => m.DedupeKey).ToListAsync()).Should().Equal("k-new");
        (await check.PushDeliveries.Select(d => d.Status).ToListAsync()).Should().Equal(PushStatus.Pending);
    }

    [Fact]
    public async Task Job_KeepsGoingWhenOneTaskFailsAsync()
    {
        var ran = new RecordingTask();
        var job = new DataRetentionJob([new FailingTask(), ran], new FakeClock(_nowUtc), NullLogger<DataRetentionJob>.Instance);

        await job.ExecuteAsync(CancellationToken.None);

        ran.Ran.Should().BeTrue();
    }

    [Fact]
    public async Task BatchDelete_RemovesMoreRowsThanOneBatchAsync()
    {
        await using (var db = Identity())
        {
            for (var i = 0; i < DataRetentionRules.BatchSize + 5; i++)
            {
                db.OutboxMessages.Add(Outbox($"bulk-{i}", _nowUtc.AddDays(-30)));
            }

            await db.SaveChangesAsync();
        }

        await using (var db = Identity())
        {
            (await db.OutboxMessages.Where(m => m.Type.StartsWith("bulk-")).DeleteInBatchesAsync()).Should().Be(DataRetentionRules.BatchSize + 5);
        }
    }

    private async Task<Guid> SeedUserAsync()
    {
        var user = new UserBuilder().WithPhoneNumber($"+2010{Random.Shared.Next(10_000_000, 99_999_999)}").Build();
        await using var db = Identity();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static RefreshToken Token(Guid userId, string hash, DateTime expiresAtUtc, Guid? familyId = null) =>
        RefreshToken.Create(Guid.NewGuid(), userId, hash, familyId ?? Guid.NewGuid(), expiresAtUtc, expiresAtUtc.AddDays(-7));

    private static OutboxMessage Outbox(string type, DateTime? processedOnUtc)
    {
        var message = new OutboxMessage { Type = type, Content = "{}", OccurredOnUtc = (processedOnUtc ?? _nowUtc).AddMinutes(-1) };
        if (processedOnUtc is { } processed)
        {
            message.MarkAsProcessed(processed);
        }

        return message;
    }

    private static InboxMessage Inbox(string handler, DateTime processedOnUtc)
    {
        var message = new InboxMessage { EventId = Guid.NewGuid(), HandlerName = handler, ReceivedOnUtc = processedOnUtc };
        message.MarkAsProcessed(processedOnUtc);
        return message;
    }

    private IdentityDbContext Identity() => new(new DbContextOptionsBuilder<IdentityDbContext>()
        .UseNpgsql(_connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema))
        .Options);

    private NotificationsDbContext Notifications() => new(new DbContextOptionsBuilder<NotificationsDbContext>()
        .UseNpgsql(_connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", NotificationsDbContext.Schema))
        .Options);

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FailingTask : IDataRetentionTask
    {
        public string Name => "failing";

        public Task<int> PurgeAsync(DateTime nowUtc, CancellationToken cancellationToken = default) => throw new InvalidOperationException("boom");
    }

    private sealed class RecordingTask : IDataRetentionTask
    {
        public bool Ran { get; private set; }

        public string Name => "recording";

        public Task<int> PurgeAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            Ran = true;
            return Task.FromResult(0);
        }
    }
}
