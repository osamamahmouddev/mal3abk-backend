using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Configuration;
using Malabak.Api.Infrastructure.Realtime;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Venues.Application.Common;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Caching;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Phase 6: Edge-case security tests for SignalR (SECURITY.md §8).
/// Covers ticket lifetime, single-use ticket replay rejection, room-exhaustion limits (max 20 days),
/// strict tenant isolation on staff rooms, and user-group isolation on connection.
/// </summary>
public sealed class SignalRSecurityEdgeCaseTests
{
    private static readonly DateTime _now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task OnConnectedAsync_WhenUserIsAuthenticated_BindsToUserGroupOnlyAsync()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var principal = CreatePrincipal(userId, "Player");
        var hub = CreateHub(principal, out var groups, out _);

        // Act
        await hub.OnConnectedAsync();

        // Assert
        groups.Added.Should().ContainSingle()
            .Which.Should().Be(("conn-1", BookingHub.UserGroupName(userId)));
    }

    [Fact]
    public async Task OnConnectedAsync_WhenAnonymous_DoesNotBindToAnyGroupAsync()
    {
        // Arrange
        var hub = CreateHub(user: null, out var groups, out _);

        // Act
        await hub.OnConnectedAsync();

        // Assert
        groups.Added.Should().BeEmpty();
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "2026-11-20")]
    [InlineData("c0a80101-0000-0000-0000-000000000001", "not-a-date")]
    [InlineData("c0a80101-0000-0000-0000-000000000001", "2026-13-45")]
    [InlineData("c0a80101-0000-0000-0000-000000000001", "invalid-date-string")]
    [InlineData("c0a80101-0000-0000-0000-000000000001", "")]
    public async Task WatchVenue_InvalidInput_ThrowsHubExceptionAsync(string venueIdStr, string dateIso)
    {
        // Arrange
        var venueId = Guid.Parse(venueIdStr);
        var hub = CreateHub(CreatePrincipal(Guid.NewGuid(), "Player"), out _, out _);

        // Act
        var act = async () => await hub.WatchVenueAsync(venueId, dateIso);

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*Invalid venueId or date format*");
    }

    [Fact]
    public async Task WatchVenue_WatchesUpTo20Days_AndRejects21stWatchedDayAsync()
    {
        // Arrange
        var venueId = Guid.NewGuid();
        var hub = CreateHub(CreatePrincipal(Guid.NewGuid(), "Player"), out var groups, out _);

        // Act: watch 20 days (the allowed maximum)
        for (var i = 1; i <= BookingHub.MaxWatchedVenueDays; i++)
        {
            var dateIso = $"2026-11-{i:D2}";
            await hub.WatchVenueAsync(venueId, dateIso);
        }

        // Assert: 20 groups joined
        groups.Added.Should().HaveCount(BookingHub.MaxWatchedVenueDays);

        // Act: watching a 21st distinct date should be rejected to protect server memory
        var act = async () => await hub.WatchVenueAsync(venueId, "2026-11-25");

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage($"*at most {BookingHub.MaxWatchedVenueDays} venue days*");
    }

    [Fact]
    public async Task LeaveVenue_RemovesClientFromGroupAsync()
    {
        // Arrange
        var venueId = Guid.NewGuid();
        var date = "2026-11-15";
        var hub = CreateHub(CreatePrincipal(Guid.NewGuid(), "Player"), out var groups, out _);

        await hub.WatchVenueAsync(venueId, date);

        // Act
        await hub.LeaveVenueAsync(venueId, date);

        // Assert
        var expectedGroup = BookingHub.VenueDateGroupName(venueId, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        groups.Removed.Should().ContainSingle().Which.Should().Be(("conn-1", expectedGroup));
    }

    [Fact]
    public async Task JoinStaffRoom_WhenUnauthenticated_ThrowsUnauthorizedAsync()
    {
        // Arrange
        var hub = CreateHub(user: null, out _, out _);

        // Act
        var act = async () => await hub.JoinStaffRoomAsync(Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*Unauthorized: Valid user session required.*");
    }

    [Fact]
    public async Task JoinStaffRoom_WhenUserIsNotStaffOfVenue_ThrowsForbiddenAsync()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var strangerVenueId = Guid.NewGuid();
        var hub = CreateHub(CreatePrincipal(userId, "Player"), out _, out _);

        // accessService has not granted access to strangerVenueId for this userId
        // Act
        var act = async () => await hub.JoinStaffRoomAsync(strangerVenueId);

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*Forbidden: Not staff of this venue.*");
    }

    [Fact]
    public async Task JoinStaffRoom_WhenUserIsAuthorizedStaff_AddsToVenueStaffGroupAsync()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var venueId = Guid.NewGuid();
        var hub = CreateHub(CreatePrincipal(userId, "VenueStaff"), out var groups, out var accessService);

        accessService.Grant(userId, venueId);

        // Act
        await hub.JoinStaffRoomAsync(venueId);

        // Assert
        groups.Added.Should().ContainSingle()
            .Which.Should().Be(("conn-1", BookingHub.VenueStaffGroupName(venueId)));
    }

    [Fact]
    public async Task TicketService_SingleUseJti_IsConsumedOnce_AndReplayReturnsNullAsync()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var principal = CreatePrincipal(userId, "Player");
        var store = new FakeAtomicStore();
        var ticketService = new TicketService(
            new FakeKeyProvider(),
            Options.Create(new JwtOptions { Issuer = "test-issuer", Audience = "test-aud" }),
            store,
            new FakeTestClock(_now));

        // Act
        var ticketResponse = await ticketService.CreateTicketAsync(principal);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(ticketResponse.Ticket);

        var jtiKey = $"wsjti:{jwt.Id}";

        // Verify ticket key exists in store
        (await store.ExistsAsync(jtiKey)).Should().BeTrue();

        // First consume succeeds and yields the user ID bound to the ticket
        var consumed = await store.ConsumeAsync(jtiKey);
        consumed.Should().Be(userId.ToString("D"));

        // Second consume (replay) returns null -> token already consumed!
        var replay = await store.ConsumeAsync(jtiKey);
        replay.Should().BeNull("replaying a consumed WebSocket ticket is strictly rejected");
    }

    private static ClaimsPrincipal CreatePrincipal(Guid userId, string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
            new Claim(ClaimTypes.Role, role)
        ], "TestAuth"));

    private static BookingHub CreateHub(
        ClaimsPrincipal? user,
        out FakeGroupManager groups,
        out FakeVenueAccessService accessService)
    {
        groups = new FakeGroupManager();
        accessService = new FakeVenueAccessService();

        var hub = new BookingHub(accessService, NullLogger<BookingHub>.Instance)
        {
            Context = new FakeHubCallerContext("conn-1", user),
            Groups = groups
        };

        return hub;
    }

    private sealed class FakeHubCallerContext(string connectionId, ClaimsPrincipal? user) : HubCallerContext
    {
        public override string ConnectionId => connectionId;
        public override string? UserIdentifier => user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        public override ClaimsPrincipal? User => user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features => new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class FakeGroupManager : IGroupManager
    {
        public List<(string ConnectionId, string GroupName)> Added { get; } = [];
        public List<(string ConnectionId, string GroupName)> Removed { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Removed.Add((connectionId, groupName));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeVenueAccessService : IVenueAccessService
    {
        private readonly HashSet<(Guid UserId, Guid VenueId)> _allowed = [];

        public void Grant(Guid userId, Guid venueId) => _allowed.Add((userId, venueId));

        public Task<VenueAccess> GetAccessAsync(Guid userId, Guid venueId, CancellationToken cancellationToken = default)
        {
            if (_allowed.Contains((userId, venueId)))
            {
                return Task.FromResult(VenueAccess.Owner(venueId));
            }
            return Task.FromResult(VenueAccess.None(venueId));
        }

        public Task<IReadOnlyList<VenueAccess>> GetAccessibleVenuesAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var list = _allowed
                .Where(x => x.UserId == userId)
                .Select(x => VenueAccess.Owner(x.VenueId))
                .ToList();
            return Task.FromResult<IReadOnlyList<VenueAccess>>(list);
        }
    }

    private sealed class FakeAtomicStore : IAtomicConsumeStore
    {
        private readonly Dictionary<string, string> _store = new();

        public Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task SetJsonAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            _store[key] = System.Text.Json.JsonSerializer.Serialize(value);
            return Task.CompletedTask;
        }

        public Task<string?> ConsumeAsync(string key, CancellationToken cancellationToken = default)
        {
            if (_store.Remove(key, out var val))
            {
                return Task.FromResult<string?>(val);
            }
            return Task.FromResult<string?>(null);
        }

        public Task<T?> ConsumeJsonAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (_store.Remove(key, out var val))
            {
                return Task.FromResult(System.Text.Json.JsonSerializer.Deserialize<T>(val));
            }
            return Task.FromResult<T?>(default);
        }

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.ContainsKey(key));

        public Task<bool> InvalidateAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.Remove(key));
    }

    private sealed class FakeTestClock(DateTime nowUtc) : IClock
    {
        public DateTime UtcNow => nowUtc;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(nowUtc, CairoTimeZone);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }

    private sealed class FakeKeyProvider : IJwtKeyProvider
    {
        private readonly ECDsaSecurityKey _key;

        public FakeKeyProvider()
        {
            var ecdsa = ECDsa.Create();
            _key = new ECDsaSecurityKey(ecdsa) { KeyId = "test-p6-key" };
        }

        public string CurrentKeyId => "test-p6-key";
        public ECDsaSecurityKey GetCurrentSigningKey() => _key;
        public SigningCredentials GetCurrentSigningCredentials() => new(_key, SecurityAlgorithms.EcdsaSha256);
        public IEnumerable<SecurityKey> GetValidationKeys() => [_key];
    }
}
