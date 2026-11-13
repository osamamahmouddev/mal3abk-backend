using System.Net;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Malabak.Api.Infrastructure.Configuration;
using Malabak.Api.Infrastructure.Idempotency;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Caching;
using Malabak.SharedKernel.Locking;
using Malabak.SharedKernel.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// The Idempotency-Key is client-chosen, so it must only ever be honoured within the caller's own scope:
/// another user sending the same key must neither be replayed the first user's response nor get a body-mismatch error.
/// </summary>
public sealed class IdempotencyUserScopingTests
{
    private const string _body = """{"id":"a","value":1}""";
    private const string _otherBody = """{"id":"b","value":2}""";

    private readonly InMemoryIdempotencyStore _store = new();
    private int _executions;

    private IdempotentEndpointFilter CreateFilter() => new(
        _store,
        new Sha256RequestHasher(),
        new NoopLockProvider(),
        new RedisKeyConventions("test"),
        new SystemClock(),
        Options.Create(new IdempotencyOptions()),
        NullLogger<IdempotentEndpointFilter>.Instance);

    private async Task<(object? Result, HttpContext Http)> SendAsync(string key, string body, Guid? userId, string ip = "10.0.0.1")
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = "/api/v1/bookings";
        http.Request.Headers["Idempotency-Key"] = key;
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (userId is { } id)
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test"));
        }

        var context = new DefaultEndpointFilterInvocationContext(http);
        var result = await CreateFilter().InvokeAsync(context, _ =>
        {
            _executions++;
            return ValueTask.FromResult<object?>(Results.Created("/x", new { n = _executions }));
        });

        return (result, http);
    }

    [Fact]
    public async Task SameKeyAndBody_FromAnotherUser_IsExecutedNotReplayedAsync()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await SendAsync("shared-key", _body, userA);
        var (_, http) = await SendAsync("shared-key", _body, userB);

        _executions.Should().Be(2, "user B's request must run on its own, not be answered with user A's stored response");
        http.Response.Headers.ContainsKey("Idempotent-Replayed").Should().BeFalse();
    }

    [Fact]
    public async Task SameKeyAndDifferentBody_FromAnotherUser_IsNotRejectedAsMismatchAsync()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await SendAsync("shared-key", _body, userA);
        var (result, _) = await SendAsync("shared-key", _otherBody, userB);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status201Created);
        _executions.Should().Be(2);
    }

    [Fact]
    public async Task SameUser_SameKeyAndBody_StillReplaysAsync()
    {
        var userA = Guid.NewGuid();

        await SendAsync("shared-key", _body, userA);
        var (_, http) = await SendAsync("shared-key", _body, userA);

        _executions.Should().Be(1);
        http.Response.Headers["Idempotent-Replayed"].ToString().Should().Be("true");
    }

    [Fact]
    public async Task SameUser_SameKeyAndDifferentBody_StillReturns422Async()
    {
        var userA = Guid.NewGuid();

        await SendAsync("shared-key", _body, userA);
        var (result, _) = await SendAsync("shared-key", _otherBody, userA);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task Anonymous_SameKeyFromDifferentIps_AreIndependentAsync()
    {
        await SendAsync("shared-key", _body, userId: null, ip: "10.0.0.1");
        await SendAsync("shared-key", _otherBody, userId: null, ip: "10.0.0.2");

        _executions.Should().Be(2);
    }

    [Fact]
    public async Task Anonymous_SameKeySameIp_StillReplaysAsync()
    {
        await SendAsync("shared-key", _body, userId: null, ip: "10.0.0.1");
        var (_, http) = await SendAsync("shared-key", _body, userId: null, ip: "10.0.0.1");

        _executions.Should().Be(1);
        http.Response.Headers["Idempotent-Replayed"].ToString().Should().Be("true");
    }

    [Fact]
    public async Task RawClientKey_IsNeverStoredAsIsAsync()
    {
        await SendAsync("raw-client-key", _body, Guid.NewGuid());

        _store.Keys.Should().ContainSingle().Which.Should().NotContain("raw-client-key");
    }

    private sealed class SystemClock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }

    private sealed class InMemoryIdempotencyStore : IIdempotencyStore
    {
        private readonly Dictionary<string, IdempotencyRecord> _records = [];

        public IReadOnlyCollection<string> Keys => _records.Keys;

        public Task<IdempotencyRecord?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_records.GetValueOrDefault(key));

        public Task CreatePendingAsync(IdempotencyRecord record, CancellationToken cancellationToken = default)
        {
            _records[record.Key] = record;
            return Task.CompletedTask;
        }

        public Task CompleteAsync(string key, int statusCode, string? responseContentType, string? responseBody, CancellationToken cancellationToken = default)
        {
            _records[key].MarkAsCompleted(statusCode, responseContentType, responseBody);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            _records.Remove(key);
            return Task.CompletedTask;
        }

        public Task<int> CleanupExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class NoopLockProvider : IDistributedLockProvider
    {
        public Task<IDistributedLock?> TryAcquireLockAsync(string resource, TimeSpan expiry, TimeSpan timeout = default, CancellationToken cancellationToken = default) =>
            Task.FromResult<IDistributedLock?>(new NoopLock(resource));

        public Task<IDistributedLock> AcquireLockAsync(string resource, TimeSpan expiry, TimeSpan timeout = default, CancellationToken cancellationToken = default) =>
            Task.FromResult<IDistributedLock>(new NoopLock(resource));
    }

    private sealed class NoopLock(string resource) : IDistributedLock
    {
        public string Resource { get; } = resource;
        public string LockToken => "t";
        public bool IsAcquired => true;
        public DateTime AcquiredAtUtc => DateTime.UtcNow;
        public TimeSpan Expiry => TimeSpan.FromSeconds(30);
        public Task<bool> ReleaseAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
