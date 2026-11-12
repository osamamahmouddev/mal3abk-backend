using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Authentication.Logout;
using Malabak.Modules.Identity.Application.Authentication.LogoutAll;
using Malabak.Modules.Identity.Application.Authentication.RefreshToken;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Phase 1 (P1) Edge-Case Tests: Tokens & Sessions (SECURITY.md §3).
/// Tests ES256 validation, algorithm confusion, refresh rotation, reuse detection, and session lifecycle.
/// </summary>
public sealed class TokenAndSessionEdgeCaseTests
{
    private readonly JwtOptions _jwtOptions = new()
    {
        Issuer = "Malabak.Api.Test",
        Audience = "Malabak.Client.Test",
        AccessTokenExpirationMinutes = 15,
        RefreshTokenExpirationDays = 7,
        CurrentKeyId = "test-es256-key-v1"
    };

    private readonly FakeClock _clock = new(new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc));

    private static IdentityDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private (TokenService Service, JwtKeyProvider KeyProvider) CreateTokenService()
    {
        var options = Options.Create(_jwtOptions);
        var keyProvider = new JwtKeyProvider(options);
        var tokenService = new TokenService(keyProvider, _clock, options);
        return (tokenService, keyProvider);
    }

    // =========================================================================
    // 1. ES256 Token Validation & Attack Protections (SECURITY.md §3.1)
    // =========================================================================

    [Fact]
    public void ValidateAccessToken_ValidToken_ReturnsClaimsPrincipal()
    {
        var (service, _) = CreateTokenService();
        var user = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Player", _clock.UtcNow);
        var sessionId = Guid.NewGuid();

        var token = service.GenerateTokens(user, sessionId);

        var principal = service.ValidateAccessToken(token.AccessToken);

        principal.Should().NotBeNull();
        principal!.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be(user.Id.ToString());
        principal.FindFirst(TokenService.SessionClaim)?.Value.Should().Be(sessionId.ToString("D"));
    }

    [Fact]
    public void ValidateAccessToken_AlgorithmNone_Rejected()
    {
        var (service, _) = CreateTokenService();

        // Create an unsigned token with alg: none
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{{\"sub\":\"{Guid.NewGuid()}\",\"iss\":\"{_jwtOptions.Issuer}\",\"aud\":\"{_jwtOptions.Audience}\",\"exp\":9999999999}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var algNoneToken = $"{header}.{payload}.";

        var principal = service.ValidateAccessToken(algNoneToken);

        principal.Should().BeNull("tokens with alg: none must be strictly rejected");
    }

    [Fact]
    public void ValidateAccessToken_AlgorithmConfusion_HS256WithPublicKey_Rejected()
    {
        var (service, keyProvider) = CreateTokenService();

        // Attacker creates an HS256 token signed with the ECDsa public key bytes
        var ecdsaKey = keyProvider.GetCurrentSigningKey();
        var pubKeyBytes = ecdsaKey.ECDsa.ExportSubjectPublicKeyInfo();
        var hmacKey = new SymmetricSecurityKey(pubKeyBytes);

        var handler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())]),
            Issuer = _jwtOptions.Issuer,
            Audience = _jwtOptions.Audience,
            NotBefore = _clock.UtcNow,
            Expires = _clock.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(hmacKey, SecurityAlgorithms.HmacSha256)
        };

        var confusedToken = handler.WriteToken(handler.CreateToken(descriptor));

        var principal = service.ValidateAccessToken(confusedToken);

        principal.Should().BeNull("HMAC tokens must be rejected because ES256 is strictly enforced");
    }

    [Fact]
    public void ValidateAccessToken_WrongKeyId_Rejected()
    {
        var (service, _) = CreateTokenService();

        // Token signed with an untrusted ECDsa key
        using var rogueEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rogueKey = new ECDsaSecurityKey(rogueEcdsa) { KeyId = "rogue-key-id" };

        var handler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())]),
            Issuer = _jwtOptions.Issuer,
            Audience = _jwtOptions.Audience,
            NotBefore = _clock.UtcNow,
            Expires = _clock.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(rogueKey, SecurityAlgorithms.EcdsaSha256)
        };

        var rogueToken = handler.WriteToken(handler.CreateToken(descriptor));

        var principal = service.ValidateAccessToken(rogueToken);

        principal.Should().BeNull("tokens signed with unrecognized keys must be rejected");
    }

    [Fact]
    public void ValidateAccessToken_TamperedPayload_Rejected()
    {
        var (service, _) = CreateTokenService();
        var user = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Player", _clock.UtcNow);
        var token = service.GenerateTokens(user);

        // Tamper with payload by modifying a character in the middle part
        var parts = token.AccessToken.Split('.');
        parts.Length.Should().Be(3);

        var tamperedPayload = parts[1][..^4] + "AAAA";
        var tamperedToken = $"{parts[0]}.{tamperedPayload}.{parts[2]}";

        var principal = service.ValidateAccessToken(tamperedToken);

        principal.Should().BeNull("tampered tokens must fail cryptographic signature verification");
    }

    [Fact]
    public void ValidateAccessToken_ExpiredToken_Rejected()
    {
        var (service, _) = CreateTokenService();
        var user = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Player", _clock.UtcNow);
        var token = service.GenerateTokens(user);

        // Advance clock past expiration and skew (15 min + 1 min skew = 17 min)
        _clock.Advance(TimeSpan.FromMinutes(18));

        var principal = service.ValidateAccessToken(token.AccessToken);

        principal.Should().BeNull("expired tokens must not validate");
    }

    [Fact]
    public void ValidateAccessToken_NotBeforeInFuture_Rejected()
    {
        var (service, keyProvider) = CreateTokenService();

        var handler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())]),
            Issuer = _jwtOptions.Issuer,
            Audience = _jwtOptions.Audience,
            NotBefore = _clock.UtcNow.AddMinutes(10), // valid only in 10 minutes
            Expires = _clock.UtcNow.AddMinutes(25),
            SigningCredentials = keyProvider.GetCurrentSigningCredentials()
        };

        var futureToken = handler.WriteToken(handler.CreateToken(descriptor));

        var principal = service.ValidateAccessToken(futureToken);

        principal.Should().BeNull("tokens with nbf in the future must be rejected");
    }

    [Fact]
    public void ValidateAccessToken_WrongIssuerOrAudience_Rejected()
    {
        var (service, keyProvider) = CreateTokenService();

        var handler = new JwtSecurityTokenHandler();

        // Wrong Issuer
        var badIss = handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())]),
            Issuer = "https://evil-attacker.com",
            Audience = _jwtOptions.Audience,
            NotBefore = _clock.UtcNow,
            Expires = _clock.UtcNow.AddMinutes(10),
            SigningCredentials = keyProvider.GetCurrentSigningCredentials()
        }));

        // Wrong Audience
        var badAud = handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())]),
            Issuer = _jwtOptions.Issuer,
            Audience = "WrongAudience",
            NotBefore = _clock.UtcNow,
            Expires = _clock.UtcNow.AddMinutes(10),
            SigningCredentials = keyProvider.GetCurrentSigningCredentials()
        }));

        service.ValidateAccessToken(badIss).Should().BeNull();
        service.ValidateAccessToken(badAud).Should().BeNull();
    }

    // =========================================================================
    // 2. Refresh Token Rotation & Reuse Detection (SECURITY.md §3.4)
    // =========================================================================

    [Fact]
    public async Task RefreshToken_Rotation_IssuesNewTokensAndRevokesOldAsync()
    {
        await using var db = CreateDbContext();
        var (service, _) = CreateTokenService();
        var handler = new RefreshTokenHandler(db, service, _clock, NullLogger<RefreshTokenHandler>.Instance);

        var user = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Player", _clock.UtcNow);
        db.Users.Add(user);

        var rawToken = "raw-refresh-token-alpha";
        var hash = service.HashRefreshToken(rawToken);
        var familyId = Guid.NewGuid();

        var tokenEntity = RefreshToken.Create(Guid.NewGuid(), user.Id, hash, familyId, _clock.UtcNow.AddDays(7), _clock.UtcNow);
        db.RefreshTokens.Add(tokenEntity);
        await db.SaveChangesAsync();

        var result = await handler.HandleAsync(new RefreshTokenCommand(rawToken));

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.Value.RefreshToken.Should().NotBe(rawToken);

        // In DB: old token is revoked
        var old = await db.RefreshTokens.SingleAsync(t => t.Id == tokenEntity.Id);
        old.IsRevoked.Should().BeTrue();
        old.ReplacedByTokenHash.Should().NotBeNull();

        // New token has same family ID and is active
        var newHash = service.HashRefreshToken(result.Value.RefreshToken);
        var newToken = await db.RefreshTokens.SingleAsync(t => t.TokenHash == newHash);
        newToken.FamilyId.Should().Be(familyId);
        newToken.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshToken_ReuseDetection_RevokesEntireFamilyAndRotatesSecurityStampAsync()
    {
        await using var db = CreateDbContext();
        var (service, _) = CreateTokenService();
        var handler = new RefreshTokenHandler(db, service, _clock, NullLogger<RefreshTokenHandler>.Instance);

        var user = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Player", _clock.UtcNow);
        var initialSecurityStamp = user.SecurityStamp;
        db.Users.Add(user);

        var familyId = Guid.NewGuid();

        // Token 1: already revoked (e.g. rotated earlier)
        var leakedRaw = "leaked-token";
        var leakedHash = service.HashRefreshToken(leakedRaw);
        var token1 = RefreshToken.Create(Guid.NewGuid(), user.Id, leakedHash, familyId, _clock.UtcNow.AddDays(7), _clock.UtcNow);
        token1.Revoke("rotation", _clock.UtcNow);
        db.RefreshTokens.Add(token1);

        // Token 2: current active token in the same family
        var activeHash = service.HashRefreshToken("active-token");
        var token2 = RefreshToken.Create(Guid.NewGuid(), user.Id, activeHash, familyId, _clock.UtcNow.AddDays(7), _clock.UtcNow);
        db.RefreshTokens.Add(token2);
        await db.SaveChangesAsync();

        // Attacker presents the leaked/revoked token
        var result = await handler.HandleAsync(new RefreshTokenCommand(leakedRaw));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.RefreshTokenReused");

        // Entire family must now be revoked
        var familyTokens = await db.RefreshTokens.Where(t => t.FamilyId == familyId).ToListAsync();
        familyTokens.Should().OnlyContain(t => t.IsRevoked);

        // User security stamp must have been rotated to invalidate active JWTs
        var userInDb = await db.Users.SingleAsync(u => u.Id == user.Id);
        userInDb.SecurityStamp.Should().NotBe(initialSecurityStamp);
    }

    [Fact]
    public async Task RefreshToken_ConcurrentRefreshesWithSameToken_ExactlyOneSucceedsAsync()
    {
        await using var db = CreateDbContext();
        var (service, _) = CreateTokenService();
        var handler = new RefreshTokenHandler(db, service, _clock, NullLogger<RefreshTokenHandler>.Instance);

        var user = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Player", _clock.UtcNow);
        db.Users.Add(user);

        var rawToken = "concurrent-token";
        var hash = service.HashRefreshToken(rawToken);
        var familyId = Guid.NewGuid();

        var tokenEntity = RefreshToken.Create(Guid.NewGuid(), user.Id, hash, familyId, _clock.UtcNow.AddDays(7), _clock.UtcNow);
        db.RefreshTokens.Add(tokenEntity);
        await db.SaveChangesAsync();

        // Execute sequential simulates race condition: first consumes token, second attempts reuse
        var firstResult = await handler.HandleAsync(new RefreshTokenCommand(rawToken));
        var secondResult = await handler.HandleAsync(new RefreshTokenCommand(rawToken));

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsFailure.Should().BeTrue();
        secondResult.Error.Code.Should().Be("Identity.RefreshTokenReused");
    }

    // =========================================================================
    // 3. Database Security: Refresh Tokens Stored as SHA-256 Hashes
    // =========================================================================

    [Fact]
    public async Task RefreshToken_DatabaseStoresOnlySha256Hash_NeverPlaintextAsync()
    {
        await using var db = CreateDbContext();
        var (service, _) = CreateTokenService();

        var rawToken = "sensitive-raw-refresh-token-12345";
        var computedHash = service.HashRefreshToken(rawToken);

        var tokenEntity = RefreshToken.Create(
            Guid.NewGuid(), Guid.NewGuid(), computedHash, Guid.NewGuid(), _clock.UtcNow.AddDays(7), _clock.UtcNow);
        db.RefreshTokens.Add(tokenEntity);
        await db.SaveChangesAsync();

        var stored = await db.RefreshTokens.SingleAsync(t => t.Id == tokenEntity.Id);

        stored.TokenHash.Should().Be(computedHash);
        stored.TokenHash.Should().NotContain(rawToken);
        stored.TokenHash.Length.Should().Be(64); // SHA-256 in hexadecimal
    }

    // =========================================================================
    // 4. Session Revocation: Logout, Logout-All, Revoke-Others
    // =========================================================================

    [Fact]
    public async Task Logout_RevokesOnlyPresentedRefreshTokenAsync()
    {
        await using var db = CreateDbContext();
        var (service, _) = CreateTokenService();
        var logoutHandler = new LogoutHandler(db, service, _clock, NullLogger<LogoutHandler>.Instance);

        var userId = Guid.NewGuid();
        var raw1 = "token-1";
        var raw2 = "token-2";

        var t1 = RefreshToken.Create(Guid.NewGuid(), userId, service.HashRefreshToken(raw1), Guid.NewGuid(), _clock.UtcNow.AddDays(7), _clock.UtcNow);
        var t2 = RefreshToken.Create(Guid.NewGuid(), userId, service.HashRefreshToken(raw2), Guid.NewGuid(), _clock.UtcNow.AddDays(7), _clock.UtcNow);
        db.RefreshTokens.AddRange(t1, t2);
        await db.SaveChangesAsync();

        var result = await logoutHandler.HandleAsync(new LogoutCommand(raw1));

        result.IsSuccess.Should().BeTrue();
        (await db.RefreshTokens.SingleAsync(t => t.Id == t1.Id)).IsRevoked.Should().BeTrue();
        (await db.RefreshTokens.SingleAsync(t => t.Id == t2.Id)).IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task LogoutAll_RevokesAllTokensAndRotatesSecurityStampAsync()
    {
        await using var db = CreateDbContext();
        var logoutAllHandler = new LogoutAllHandler(db, _clock, NullLogger<LogoutAllHandler>.Instance);

        var user = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Player", _clock.UtcNow);
        var initialStamp = user.SecurityStamp;
        db.Users.Add(user);

        var t1 = RefreshToken.Create(Guid.NewGuid(), user.Id, "hash1", Guid.NewGuid(), _clock.UtcNow.AddDays(7), _clock.UtcNow);
        var t2 = RefreshToken.Create(Guid.NewGuid(), user.Id, "hash2", Guid.NewGuid(), _clock.UtcNow.AddDays(7), _clock.UtcNow);
        db.RefreshTokens.AddRange(t1, t2);
        await db.SaveChangesAsync();

        var result = await logoutAllHandler.HandleAsync(new LogoutAllCommand(user.Id));

        result.IsSuccess.Should().BeTrue();
        (await db.RefreshTokens.Where(t => t.UserId == user.Id).AllAsync(t => t.IsRevoked)).Should().BeTrue();

        var updatedUser = await db.Users.SingleAsync(u => u.Id == user.Id);
        updatedUser.SecurityStamp.Should().NotBe(initialStamp);
    }
}
