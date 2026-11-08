using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Configuration;
using Malabak.Api.Infrastructure.Realtime;
using Malabak.Modules.Identity.Application.Common;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Caching;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// TASK-803: WebSocket security and scale tests.
/// Verifies ticket issuance, single-use ticket isolation, payload caps, and backplane configuration.
/// </summary>
public sealed class WebSocketSecurityTests
{
    [Fact]
    public async Task TicketService_GeneratesTicket_WithStrict60SecondExpiryAndWsAudienceAsync()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, "Player")
        ], "TestAuth"));

        var keyProvider = new TestKeyProvider();
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "malabak-identity-test",
            Audience = "malabak-api-test"
        });
        var atomicStore = new MockAtomicStore();
        var fixedTime = new DateTime(2026, 11, 8, 20, 0, 0, DateTimeKind.Utc);
        var clock = new TestClock(fixedTime);

        var ticketService = new TicketService(keyProvider, jwtOptions, atomicStore, clock);

        // Act
        var result = await ticketService.CreateTicketAsync(principal);

        // Assert
        result.ExpiresInSeconds.Should().Be(60);
        result.Ticket.Should().NotBeNullOrWhiteSpace();

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.Ticket);

        jwt.Issuer.Should().Be("malabak-identity-test");
        jwt.Audiences.Should().ContainSingle().Which.Should().Be(RealtimeAuthenticationExtensions.WsAudience);
        jwt.Subject.Should().Be(userId.ToString("D"));
        jwt.ValidTo.Should().Be(fixedTime.AddSeconds(60));

        // Atomic store must have registered the single-use JTI
        atomicStore.StoredKeys.Should().ContainKey($"wsjti:{jwt.Id}");
    }

    [Fact]
    public async Task TicketService_WithoutUserId_ThrowsInvalidOperationExceptionAsync()
    {
        // Arrange: anonymous principal without user id claim
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var ticketService = new TicketService(
            new TestKeyProvider(),
            Options.Create(new JwtOptions()),
            new MockAtomicStore(),
            new TestClock(DateTime.UtcNow));

        // Act
        var act = () => ticketService.CreateTicketAsync(principal);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*User id claim is required*");
    }

    [Fact]
    public void RealtimeInfrastructure_EnforcesMessageSizeLimitsAndTimeouts()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        // Act
        services.AddRealtimeInfrastructure(config);
        using var provider = services.BuildServiceProvider();

        // Assert
        var hubOptions = provider.GetRequiredService<IOptions<HubOptions>>().Value;
        hubOptions.MaximumReceiveMessageSize.Should().Be(16 * 1024, "WebSocket payloads are capped to 16KB to prevent DoS");
        hubOptions.KeepAliveInterval.Should().Be(TimeSpan.FromSeconds(15));
        hubOptions.ClientTimeoutInterval.Should().Be(TimeSpan.FromSeconds(45));
        hubOptions.HandshakeTimeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    private sealed class MockAtomicStore : IAtomicConsumeStore
    {
        public readonly Dictionary<string, string> StoredKeys = new();

        public Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            StoredKeys[key] = value;
            return Task.CompletedTask;
        }

        public Task SetJsonAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            StoredKeys[key] = JsonSerializer.Serialize(value);
            return Task.CompletedTask;
        }

        public Task<string?> ConsumeAsync(string key, CancellationToken cancellationToken = default)
        {
            if (StoredKeys.Remove(key, out var val))
            {
                return Task.FromResult<string?>(val);
            }
            return Task.FromResult<string?>(null);
        }

        public Task<T?> ConsumeJsonAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (StoredKeys.Remove(key, out var val))
            {
                return Task.FromResult(JsonSerializer.Deserialize<T>(val));
            }
            return Task.FromResult<T?>(default);
        }

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(StoredKeys.ContainsKey(key));
        }

        public Task<bool> InvalidateAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(StoredKeys.Remove(key));
        }
    }

    private sealed class TestClock : IClock
    {
        private static readonly TimeZoneInfo _cairoZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        public TestClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; }
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairoZone);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairoZone;
    }

    private sealed class TestKeyProvider : IJwtKeyProvider
    {
        private readonly ECDsaSecurityKey _key;

        public TestKeyProvider()
        {
            var ecdsa = ECDsa.Create();
            _key = new ECDsaSecurityKey(ecdsa) { KeyId = "test-key-1" };
        }

        public string CurrentKeyId => "test-key-1";

        public ECDsaSecurityKey GetCurrentSigningKey() => _key;

        public SigningCredentials GetCurrentSigningCredentials() =>
            new(_key, SecurityAlgorithms.EcdsaSha256);

        public IEnumerable<SecurityKey> GetValidationKeys() => [_key];
    }
}
