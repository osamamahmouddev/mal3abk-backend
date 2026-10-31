using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Malabak.Api.Infrastructure.Idempotency;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class IdempotentEndpointFilterIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private static bool _initialized;
    private static readonly object _initLock = new();

    public IdempotentEndpointFilterIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        EnsureDatabaseInitialized();
    }

    private void EnsureDatabaseInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (_initLock)
        {
            if (_initialized)
            {
                return;
            }

            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            // The schema comes from the real migrations, like every other test. Creating the table by hand made the
            // Identity migration fail with "already exists" whenever a migrating host started after this class.
            context.Database.Migrate();

            _initialized = true;
        }
    }

    [Fact]
    public async Task IdempotentEndpoint_WhenMissingKey_Returns400BadRequestAsync()
    {
        // Arrange
        var client = _factory.CreateClient();
        var payload = new { Id = "missing-key-1", Value = 50.0m };

        // Act (no Idempotency-Key header provided)
        var response = await client.PostAsJsonAsync("/api/v1/test/idempotent-action", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("idempotency_key_required");
    }

    [Fact]
    public async Task IdempotentEndpoint_FirstRequest_ReturnsCreatedAsync()
    {
        // Arrange
        var client = _factory.CreateClient();
        var key = Guid.NewGuid().ToString("N");
        var payload = new { Id = "test-first-1", Value = 100.0m };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/test/idempotent-action")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("Idempotency-Key", key);

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Contains("Idempotent-Replayed").Should().BeFalse();

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("test-first-1");
    }

    [Fact]
    public async Task IdempotentEndpoint_SameKeyAndSameBody_ReplaysStoredResponseAsync()
    {
        // Arrange
        var client = _factory.CreateClient();
        var key = Guid.NewGuid().ToString("N");
        var payload = new { Id = "test-replay-1", Value = 250.0m };

        // 1. Initial request
        var initialRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/test/idempotent-action")
        {
            Content = JsonContent.Create(payload)
        };
        initialRequest.Headers.Add("Idempotency-Key", key);
        var initialResponse = await client.SendAsync(initialRequest);
        initialResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var initialBody = await initialResponse.Content.ReadAsStringAsync();

        // 2. Replay request with exact same key and body
        var replayRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/test/idempotent-action")
        {
            Content = JsonContent.Create(payload)
        };
        replayRequest.Headers.Add("Idempotency-Key", key);

        // Act
        var replayResponse = await client.SendAsync(replayRequest);

        // Assert
        replayResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        replayResponse.Headers.Contains("Idempotent-Replayed").Should().BeTrue();
        replayResponse.Headers.GetValues("Idempotent-Replayed").First().Should().Be("true");

        var replayBody = await replayResponse.Content.ReadAsStringAsync();
        replayBody.Should().Be(initialBody);
    }

    [Fact]
    public async Task IdempotentEndpoint_SameKeyAndDifferentBody_Returns422UnprocessableEntityAsync()
    {
        // Arrange
        var client = _factory.CreateClient();
        var key = Guid.NewGuid().ToString("N");
        var payload1 = new { Id = "test-mismatch-1", Value = 150.0m };
        var payload2 = new { Id = "test-mismatch-2", Value = 999.0m }; // Different payload

        // 1. Initial request
        var initialRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/test/idempotent-action")
        {
            Content = JsonContent.Create(payload1)
        };
        initialRequest.Headers.Add("Idempotency-Key", key);
        var initialResponse = await client.SendAsync(initialRequest);
        initialResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // 2. Conflicting request with same key but different body
        var conflictingRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/test/idempotent-action")
        {
            Content = JsonContent.Create(payload2)
        };
        conflictingRequest.Headers.Add("Idempotency-Key", key);

        // Act
        var conflictingResponse = await client.SendAsync(conflictingRequest);

        // Assert
        conflictingResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var responseContent = await conflictingResponse.Content.ReadAsStringAsync();
        responseContent.Should().Contain("idempotency_body_mismatch");
    }

    [Fact]
    public async Task IdempotentEndpoint_ConcurrentDuplicateRequests_SerializesAndReplaysAsync()
    {
        // Arrange
        var client = _factory.CreateClient();
        var key = Guid.NewGuid().ToString("N");
        var payload = new { Id = "test-concurrent-1", Value = 75.0m };

        Func<HttpRequestMessage> createRequest = () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/test/idempotent-action")
            {
                Content = JsonContent.Create(payload)
            };
            req.Headers.Add("Idempotency-Key", key);
            return req;
        };

        // Act: send 2 requests concurrently
        var task1 = client.SendAsync(createRequest());
        var task2 = client.SendAsync(createRequest());
        var responses = await Task.WhenAll(task1, task2);

        // Assert: both should succeed (201 Created), one replayed from the other
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.Created));
        responses.Count(r => r.Headers.Contains("Idempotent-Replayed")).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task IdempotencyCleanupJob_WhenExecuted_PurgesExpiredRecordsAsync()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();

        var expiredKey = $"expired-key-{Guid.NewGuid():N}";
        var expiredRecord = new IdempotencyRecord
        {
            Key = expiredKey,
            RequestHash = "expired-hash",
            HttpMethod = "POST",
            RequestPath = "/api/v1/test",
            CreatedAtUtc = clock.UtcNow.AddHours(-30),
            ExpiresAtUtc = clock.UtcNow.AddHours(-6) // Expired 6 hours ago
        };
        expiredRecord.MarkAsCompleted(200, "application/json", "{}");
        context.IdempotencyRecords.Add(expiredRecord);
        await context.SaveChangesAsync();

        var cleanupJob = new IdempotencyCleanupJob(store, clock, NullLogger<IdempotencyCleanupJob>.Instance);

        // Act
        await cleanupJob.ExecuteAsync(CancellationToken.None);

        // Assert
        var recordInDb = await context.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key == expiredKey);
        recordInDb.Should().BeNull();
    }
}
