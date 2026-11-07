using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Identity.Application.Admin;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.Modules.Venues.Application.Availability;
using Malabak.Modules.Venues.Application.Explore;
using Malabak.Modules.Venues.Application.Featured;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>Deeper edge cases for TASK-063 (impersonation), TASK-720 (Cloudflare Access) and TASK-711 (featured venues).</summary>
public sealed class Phase6SecurityEdgeTests
{
    private static readonly DateTime _now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    private readonly Guid _admin = Guid.NewGuid();
    private readonly Recorder _recorder = new();
    private readonly Clock _clock = new();

    // ---------- TASK-063: impersonation ----------

    [Fact]
    public async Task TheTargetIsCheckedAgainAtExchange_SoASuspensionInTheMeantimeWinsAsync()
    {
        await using var db = IdentityDb();
        var player = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Ahmed", _now);
        db.Users.Add(player);
        await db.SaveChangesAsync();
        var service = Impersonation(db, new MemoryCodes());
        var code = (await service.IssueCodeAsync(_admin, player.Id, "Ticket SUP-1")).Value.Code;

        var tracked = await db.Users.SingleAsync();
        tracked.Suspend("fraud", _now);
        await db.SaveChangesAsync();

        (await service.ExchangeAsync(code)).Error.Should().Be(ImpersonationErrors.NotAllowed);
        (await db.UserSessions.CountAsync()).Should().Be(0, "no session is opened for a refused exchange");
    }

    [Fact]
    public async Task VenueOwners_CanBeHelped_AndTheTokenLastsFifteenMinutesWithoutRefreshAsync()
    {
        await using var db = IdentityDb();
        var owner = User.CreateStaff(Guid.NewGuid(), "owner@example.com", "+201012345678", "h", "Owner", UserRole.VenueOwner, _now);
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        var service = Impersonation(db, new MemoryCodes());

        var token = (await service.ExchangeAsync((await service.IssueCodeAsync(_admin, owner.Id, "Can't add a pitch")).Value.Code)).Value;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.AccessToken);

        (jwt.ValidTo - jwt.ValidFrom).Should().Be(TimeSpan.FromMinutes(15));
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role || c.Type == "role");
        jwt.Claims.Should().Contain(c => c.Type == "amr" && c.Value == "imp");
        token.ExpiresAtUtc.Should().Be(_now.AddMinutes(15));
        token.ImpersonatorId.Should().Be(_admin);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReasonsAreMandatory_AndCodesAreValidatedBeforeTheStoreIsHitAsync(string? reason)
    {
        await using var db = IdentityDb();
        var player = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Ahmed", _now);
        db.Users.Add(player);
        await db.SaveChangesAsync();
        var codes = new MemoryCodes();
        var service = Impersonation(db, codes);

        (await service.IssueCodeAsync(_admin, player.Id, reason)).Error.Should().Be(ImpersonationErrors.ReasonRequired);
        (await service.IssueCodeAsync(_admin, player.Id, new string('x', 501))).Error.Should().Be(ImpersonationErrors.ReasonRequired);
        (await service.ExchangeAsync(new string('a', 101))).Error.Should().Be(ImpersonationErrors.InvalidCode);
        (await service.ExchangeAsync(null)).Error.Should().Be(ImpersonationErrors.InvalidCode);
        codes.Takes.Should().Be(0);
        (await service.IssueCodeAsync(_admin, Guid.NewGuid(), "unknown user")).Error.Should().Be(ImpersonationErrors.NotAllowed);
    }

    [Fact]
    public async Task EachCodeIsUnique_AndOnlyItsHashIsStoredAsync()
    {
        await using var db = IdentityDb();
        var player = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Ahmed", _now);
        db.Users.Add(player);
        await db.SaveChangesAsync();
        var codes = new MemoryCodes();
        var service = Impersonation(db, codes);

        var issued = new HashSet<string>();
        for (var i = 0; i < 20; i++)
        {
            issued.Add((await service.IssueCodeAsync(_admin, player.Id, "help")).Value.Code);
        }

        issued.Should().HaveCount(20);
        issued.Should().OnlyContain(c => c.Length >= 40 && !c.Contains('+') && !c.Contains('/') && !c.Contains('='));
        codes.Stored.Keys.Should().OnlyContain(k => k.Length == 64, "SHA-256 hex");
        codes.Stored.Keys.Should().NotIntersectWith(issued);
        codes.Lifetimes.Should().OnlyContain(l => l == TimeSpan.FromSeconds(60));
    }

    [Theory]
    [InlineData("PUT", "/api/v1/me/phone-change/verify-new", true)]
    [InlineData("POST", "/api/v1/auth/logout-all", true)]
    [InlineData("POST", "/api/v1/auth/step-up", true)]
    [InlineData("POST", "/api/v1/admin/impersonate", true)]
    [InlineData("PUT", "/api/v1/venue-manager/venues/1/payment-methods/2", true)]
    [InlineData("POST", "/api/v1/venue-manager/venues/1/payment-methods/2/reveal", true)]
    [InlineData("POST", "/api/v1/me/sessions/revoke-others", true)]
    [InlineData("PUT", "/api/v1/me/language", false)]
    [InlineData("GET", "/api/v1/me/consents", false)]
    [InlineData("POST", "/api/v1/support/tickets", false)]
    [InlineData("GET", "/api/V1/AUTH/2fa", true)]
    [InlineData("GET", "/apiv1/auth", false)]
    public void TheRestrictionList_CoversSensitiveActions_CaseInsensitively(string method, string path, bool restricted)
    {
        ImpersonationRestrictionMiddleware.IsRestricted(method, new PathString(path)).Should().Be(restricted);
    }

    [Fact]
    public async Task TheMiddleware_Refuses403_OnlyForImpersonationTokensAsync()
    {
        var reached = false;
        var middleware = new ImpersonationRestrictionMiddleware(_ =>
        {
            reached = true;
            return Task.CompletedTask;
        });

        var impersonated = Context("POST", "/api/v1/auth/password/change", impersonated: true);
        await middleware.InvokeAsync(impersonated);
        impersonated.Response.StatusCode.Should().Be(403);
        reached.Should().BeFalse();

        var normal = Context("POST", "/api/v1/auth/password/change", impersonated: false);
        await middleware.InvokeAsync(normal);
        reached.Should().BeTrue();
    }

    // ---------- TASK-720: Cloudflare Access with real signed assertions ----------

    [Fact]
    public async Task AValidAccessAssertion_IsAccepted_AndEveryTamperingIsRejectedAsync()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = Options.Create(new CloudflareAccessOptions { TeamDomain = "malabak.cloudflareaccess.com", Audiences = ["aud-hangfire"] });
        var validator = new CloudflareAccessValidator(options, new CertsServer(key, "kid-1"), NullLogger<CloudflareAccessValidator>.Instance);

        (await validator.IsValidAsync(Sign(key, "kid-1", "https://malabak.cloudflareaccess.com", "aud-hangfire", DateTime.UtcNow.AddMinutes(10)), default)).Should().BeTrue();
        (await validator.IsValidAsync(Sign(key, "kid-1", "https://malabak.cloudflareaccess.com", "aud-other", DateTime.UtcNow.AddMinutes(10)), default)).Should().BeFalse("wrong audience");
        (await validator.IsValidAsync(Sign(key, "kid-1", "https://evil.cloudflareaccess.com", "aud-hangfire", DateTime.UtcNow.AddMinutes(10)), default)).Should().BeFalse("wrong issuer");
        (await validator.IsValidAsync(Sign(key, "kid-1", "https://malabak.cloudflareaccess.com", "aud-hangfire", DateTime.UtcNow.AddMinutes(-5)), default)).Should().BeFalse("expired");
        (await validator.IsValidAsync(Sign(otherKey, "kid-1", "https://malabak.cloudflareaccess.com", "aud-hangfire", DateTime.UtcNow.AddMinutes(10)), default)).Should().BeFalse("signed by someone else");
    }

    [Fact]
    public async Task TheGuard_LetsAValidAssertionThrough_ToTheHangfireDashboardAsync()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = Options.Create(new CloudflareAccessOptions { TeamDomain = "malabak.cloudflareaccess.com", Audiences = ["aud-hangfire"] });
        var validator = new CloudflareAccessValidator(options, new CertsServer(key, "kid-1"), NullLogger<CloudflareAccessValidator>.Instance);
        var reached = false;
        var guard = new CloudflareAccessGuardMiddleware(_ =>
        {
            reached = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/hangfire";
        context.Request.Headers[CloudflareAccessValidator.AssertionHeader] = Sign(key, "kid-1", "https://malabak.cloudflareaccess.com", "aud-hangfire", DateTime.UtcNow.AddMinutes(5));

        await guard.InvokeAsync(context, options, validator);

        reached.Should().BeTrue();
    }

    [Fact]
    public async Task WithoutAudiences_NothingIsAccepted_EvenAValidTokenAsync()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = Options.Create(new CloudflareAccessOptions { TeamDomain = "malabak.cloudflareaccess.com" });
        var validator = new CloudflareAccessValidator(options, new CertsServer(key, "kid-1"), NullLogger<CloudflareAccessValidator>.Instance);

        (await validator.IsValidAsync(Sign(key, "kid-1", "https://malabak.cloudflareaccess.com", "aud-hangfire", DateTime.UtcNow.AddMinutes(5)), default)).Should().BeFalse();
    }

    // ---------- TASK-711: featured venues ----------

    [Fact]
    public async Task UpdatesAndCancellations_AreValidated_AndASuspendedVenueDropsOutAsync()
    {
        await using var db = VenuesDb();
        var venue = Venue.Create(Guid.NewGuid(), "Arena", $"v-{Guid.NewGuid():N}", null, Guid.NewGuid(), "a", "Cairo", "Maadi", 29.96, 31.25, "+201000000000", null, null, _now);
        venue.SubmitForApproval(venue.OwnerId, null, _now);
        venue.Approve(_admin, null, _now);
        venue.SetPublished(true, _now);
        db.Venues.Add(venue);
        db.Pitches.Add(Pitch.Create(Guid.NewGuid(), venue.Id, "P1", SurfaceType.ArtificialTurfValue, 5, null, null, null, _now));
        await db.SaveChangesAsync();
        var flags = new Flags();
        flags.On.Add(FeatureFlagKeys.FeaturedVenues);
        var service = new FeaturedVenueService(db, Explore(db), flags, _recorder, _clock);
        var placement = (await service.CreateAsync(new FeaturedPlacementInput(venue.Id, "Explore", null, _now.AddHours(-1), _now.AddDays(2), 0), _admin)).Value;

        (await service.UpdateAsync(Guid.NewGuid(), new FeaturedPlacementInput(venue.Id, "Explore", null, _now, _now.AddDays(1), 0), _admin)).Error.Should().Be(FeaturedErrors.NotFound);
        (await service.UpdateAsync(placement.Id, new FeaturedPlacementInput(venue.Id, "Explore", null, _now, _now.AddDays(100), 0), _admin)).Error.Should().Be(FeaturedErrors.InvalidSchedule);
        (await service.UpdateAsync(placement.Id, new FeaturedPlacementInput(venue.Id, "Banner", null, _now, _now.AddDays(1), 0), _admin)).Error.Should().Be(FeaturedErrors.Invalid);
        (await service.LiveAsync(FeaturedPlacementSlot.Explore, null)).Should().ContainSingle();

        var tracked = await db.Venues.SingleAsync();
        tracked.Suspend(_admin, "complaints", _now);
        await db.SaveChangesAsync();
        (await service.LiveAsync(FeaturedPlacementSlot.Explore, null)).Should().BeEmpty("a suspended venue is not searchable, so it is never promoted");

        (await service.CancelAsync(placement.Id, _admin)).Value.IsCancelled.Should().BeTrue();
        (await service.CancelAsync(placement.Id, _admin)).Error.Should().Be(FeaturedErrors.Cancelled);
        (await service.UpdateAsync(placement.Id, new FeaturedPlacementInput(venue.Id, "Explore", null, _now, _now.AddDays(1), 0), _admin)).Error.Should().Be(FeaturedErrors.Cancelled);
        (await service.ListAsync(includeEnded: false)).Should().BeEmpty();
        (await service.ListAsync(includeEnded: true)).Should().ContainSingle();
    }

    private static string Sign(ECDsa key, string kid, string issuer, string audience, DateTime expires)
    {
        var signing = new SigningCredentials(new ECDsaSecurityKey(key) { KeyId = kid }, SecurityAlgorithms.EcdsaSha256);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = expires.AddMinutes(-30),
            NotBefore = expires.AddMinutes(-30),
            Expires = expires,
            Subject = new ClaimsIdentity([new Claim("email", "eng@malabak.app")]),
            SigningCredentials = signing
        });
    }

    private static DefaultHttpContext Context(string method, string path, bool impersonated)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()) };
        if (impersonated)
        {
            claims.Add(new Claim(TokenService.ImpersonationActorClaim, "{\"sub\":\"x\"}"));
        }

        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return context;
    }

    private ImpersonationService Impersonation(IdentityDbContext db, IImpersonationCodeStore codes)
    {
        var jwt = new JwtOptions { Issuer = "test", Audience = "test-client", AccessTokenExpirationMinutes = 15, RefreshTokenExpirationDays = 7, CurrentKeyId = "test-es256-key-v1" };
        return new ImpersonationService(db, codes, new TokenService(new JwtKeyProvider(Options.Create(jwt)), _clock, Options.Create(jwt)), new TestDevice(), _recorder, _recorder, _clock);
    }

    private VenueExplore Explore(VenuesDbContext db) =>
        new(db, new CachedVenueAvailability(new VenueAvailabilityQuery(db, new NoBookings(), _clock), new NoCache(), _clock, NullLogger<CachedVenueAvailability>.Instance), _clock);

    private static IdentityDbContext IdentityDb() => new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static VenuesDbContext VenuesDb() => new(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>Serves a JWKS for one key at the team's certs URL, like Cloudflare does.</summary>
    private sealed class CertsServer(ECDsa key, string kid) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(key, kid));

        private sealed class Handler(ECDsa key, string kid) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                request.RequestUri!.AbsolutePath.Should().Be("/cdn-cgi/access/certs");
                var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new ECDsaSecurityKey(key) { KeyId = kid });
                var json = $"{{\"keys\":[{{\"kty\":\"EC\",\"crv\":\"{jwk.Crv}\",\"x\":\"{jwk.X}\",\"y\":\"{jwk.Y}\",\"kid\":\"{kid}\",\"alg\":\"ES256\",\"use\":\"sig\"}}],\"public_cert\":{{}}}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }
        }
    }

    private sealed class MemoryCodes : IImpersonationCodeStore
    {
        public Dictionary<string, ImpersonationGrant> Stored { get; } = [];
        public List<TimeSpan> Lifetimes { get; } = [];
        public int Takes { get; private set; }

        public Task SaveAsync(string codeHash, ImpersonationGrant grant, TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            Stored[codeHash] = grant;
            Lifetimes.Add(lifetime);
            return Task.CompletedTask;
        }

        public Task<ImpersonationGrant?> TakeAsync(string codeHash, CancellationToken cancellationToken = default)
        {
            Takes++;
            return Task.FromResult(Stored.Remove(codeHash, out var grant) ? grant : null);
        }
    }

    private sealed class Flags : IFeatureFlags
    {
        public HashSet<string> On { get; } = [];

        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(On.Contains(key));
    }

    private sealed class NoBookings : IUpcomingBookingsReader
    {
        public Task<IReadOnlyList<UpcomingBookingRef>> GetActiveAsync(IReadOnlyCollection<Guid> pitchIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UpcomingBookingRef>>([]);
    }

    private sealed class NoCache : IAvailabilityCacheStore
    {
        public Task<long> GetVersionAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.FromResult(0L);

        public Task<long> IncrementVersionAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.FromResult(1L);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Recorder : IAuditLog, INotificationPublisher
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Clock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
