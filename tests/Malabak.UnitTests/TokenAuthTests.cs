using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Authentication.Logout;
using Malabak.Modules.Identity.Application.Authentication.LogoutAll;
using Malabak.Modules.Identity.Application.Authentication.RefreshToken;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Malabak.UnitTests;

public sealed class TokenAuthTests
{
    private readonly JwtOptions _jwtOptions = new()
    {
        Issuer = "Malabak.Api.Test",
        Audience = "Malabak.Client.Test",
        AccessTokenExpirationMinutes = 15,
        RefreshTokenExpirationDays = 7,
        CurrentKeyId = "test-es256-key-v1"
    };

    private readonly IClock _clock = new TestClock(new DateTime(2026, 10, 27, 20, 0, 0, DateTimeKind.Utc));

    private static IdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new IdentityDbContext(options);
    }

    [Fact]
    public void JwtKeyProvider_ShouldGenerateValidECDsaKeyWithKid()
    {
        var keyProvider = new JwtKeyProvider(Options.Create(_jwtOptions));

        keyProvider.CurrentKeyId.Should().Be("test-es256-key-v1");

        var signingKey = keyProvider.GetCurrentSigningKey();
        signingKey.Should().NotBeNull();
        signingKey.KeyId.Should().Be("test-es256-key-v1");

        var credentials = keyProvider.GetCurrentSigningCredentials();
        credentials.Algorithm.Should().Be(SecurityAlgorithms.EcdsaSha256);

        keyProvider.GetValidationKeys().Should().Contain(signingKey);
    }

    [Fact]
    public void TokenService_ShouldGenerateValidES256TokenWith15MinExpiry()
    {
        var keyProvider = new JwtKeyProvider(Options.Create(_jwtOptions));
        var tokenService = new TokenService(keyProvider, _clock, Options.Create(_jwtOptions));

        var user = User.CreatePlayer(
            id: Guid.NewGuid(),
            phoneNumber: "+201012345678",
            fullName: "Amr Zaki",
            createdAtUtc: _clock.UtcNow);

        var tokenResult = tokenService.GenerateTokens(user);

        tokenResult.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokenResult.RefreshToken.Should().NotBeNullOrWhiteSpace();
        tokenResult.ExpiresIn.Should().Be(15 * 60);
        tokenResult.RefreshTokenExpiresAtUtc.Should().Be(_clock.UtcNow.AddDays(7));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenResult.AccessToken);

        jwt.Header.Alg.Should().Be(SecurityAlgorithms.EcdsaSha256);
        jwt.Header.Kid.Should().Be("test-es256-key-v1");

        var principal = tokenService.ValidateAccessToken(tokenResult.AccessToken);
        principal.Should().NotBeNull();
        principal!.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be(user.Id.ToString());
        principal.FindFirst("phone_number")?.Value.Should().Be(user.PhoneNumber);
        principal.FindFirst(ClaimTypes.Role)?.Value.Should().Be(UserRole.Player);
        principal.FindFirst("security_stamp")?.Value.Should().Be(user.SecurityStamp);
    }

    [Fact]
    public void TokenService_HashRefreshToken_ShouldComputeExpectedSha256Hex()
    {
        var keyProvider = new JwtKeyProvider(Options.Create(_jwtOptions));
        var tokenService = new TokenService(keyProvider, _clock, Options.Create(_jwtOptions));

        var raw = "test-raw-refresh-token-12345";
        var hash1 = tokenService.HashRefreshToken(raw);
        var hash2 = tokenService.HashRefreshToken(raw);

        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64); // SHA-256 in hex
    }

    [Fact]
    public async Task RefreshTokenHandler_WhenValidTokenPresented_ShouldRotateTokenAndMaintainFamilyIdAsync()
    {
        using var dbContext = CreateDbContext();
        var keyProvider = new JwtKeyProvider(Options.Create(_jwtOptions));
        var tokenService = new TokenService(keyProvider, _clock, Options.Create(_jwtOptions));
        var handler = new RefreshTokenHandler(dbContext, tokenService, _clock, NullLogger<RefreshTokenHandler>.Instance);

        var user = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Ahmed", _clock.UtcNow);
        dbContext.Users.Add(user);

        var rawRefreshToken = "initial-raw-token";
        var hash = tokenService.HashRefreshToken(rawRefreshToken);
        var familyId = Guid.NewGuid();

        var existingToken = RefreshToken.Create(
            id: Guid.NewGuid(),
            userId: user.Id,
            tokenHash: hash,
            familyId: familyId,
            expiresAtUtc: _clock.UtcNow.AddDays(7),
            createdAtUtc: _clock.UtcNow);

        dbContext.RefreshTokens.Add(existingToken);
        await dbContext.SaveChangesAsync();

        var command = new RefreshTokenCommand(rawRefreshToken);
        var result = await handler.HandleAsync(command);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.Value.RefreshToken.Should().NotBe(rawRefreshToken);
        result.Value.ExpiresIn.Should().Be(900);

        // Verify rotation in DB
        var oldTokenInDb = await dbContext.RefreshTokens.FirstAsync(t => t.Id == existingToken.Id);
        oldTokenInDb.IsRevoked.Should().BeTrue();
        oldTokenInDb.ReplacedByTokenHash.Should().NotBeNull();

        var newHash = tokenService.HashRefreshToken(result.Value.RefreshToken);
        var newTokenInDb = await dbContext.RefreshTokens.FirstAsync(t => t.TokenHash == newHash);
        newTokenInDb.FamilyId.Should().Be(familyId); // Maintained family ID
        newTokenInDb.IsRevoked.Should().BeFalse();
        oldTokenInDb.ReplacedByTokenHash.Should().Be(newHash);
    }

    [Fact]
    public async Task RefreshTokenHandler_WhenRevokedTokenPresented_ShouldTriggerReuseDetectionAndRevokeEntireFamilyAsync()
    {
        using var dbContext = CreateDbContext();
        var keyProvider = new JwtKeyProvider(Options.Create(_jwtOptions));
        var tokenService = new TokenService(keyProvider, _clock, Options.Create(_jwtOptions));
        var handler = new RefreshTokenHandler(dbContext, tokenService, _clock, NullLogger<RefreshTokenHandler>.Instance);

        var user = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Mohamed", _clock.UtcNow);
        var originalSecurityStamp = user.SecurityStamp;
        dbContext.Users.Add(user);

        var familyId = Guid.NewGuid();

        // Token 1: already revoked (e.g. leaked / previously rotated)
        var stolenTokenRaw = "stolen-raw-token";
        var stolenTokenHash = tokenService.HashRefreshToken(stolenTokenRaw);
        var revokedToken = RefreshToken.Create(
            Guid.NewGuid(), user.Id, stolenTokenHash, familyId, _clock.UtcNow.AddDays(7), _clock.UtcNow);
        revokedToken.Revoke("legitimate_rotation", _clock.UtcNow);
        dbContext.RefreshTokens.Add(revokedToken);

        // Token 2: current active token in same family
        var activeTokenHash = tokenService.HashRefreshToken("current-active-token");
        var activeToken = RefreshToken.Create(
            Guid.NewGuid(), user.Id, activeTokenHash, familyId, _clock.UtcNow.AddDays(7), _clock.UtcNow);
        dbContext.RefreshTokens.Add(activeToken);

        await dbContext.SaveChangesAsync();

        // Attacker presents the previously revoked token
        var command = new RefreshTokenCommand(stolenTokenRaw);
        var result = await handler.HandleAsync(command);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.RefreshTokenReused");

        // Verify entire family is now revoked
        var allFamilyTokens = await dbContext.RefreshTokens.Where(t => t.FamilyId == familyId).ToListAsync();
        allFamilyTokens.Should().OnlyContain(t => t.IsRevoked);

        // Verify user security stamp was rotated
        var userInDb = await dbContext.Users.FirstAsync(u => u.Id == user.Id);
        userInDb.SecurityStamp.Should().NotBe(originalSecurityStamp);
    }

    [Fact]
    public async Task RefreshTokenHandler_WhenTokenExpired_ShouldReturnExpiredErrorAsync()
    {
        using var dbContext = CreateDbContext();
        var keyProvider = new JwtKeyProvider(Options.Create(_jwtOptions));
        var tokenService = new TokenService(keyProvider, _clock, Options.Create(_jwtOptions));
        var handler = new RefreshTokenHandler(dbContext, tokenService, _clock, NullLogger<RefreshTokenHandler>.Instance);

        var user = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Kareem", _clock.UtcNow);
        dbContext.Users.Add(user);

        var expiredTokenRaw = "expired-token";
        var expiredHash = tokenService.HashRefreshToken(expiredTokenRaw);
        var expiredToken = RefreshToken.Create(
            Guid.NewGuid(), user.Id, expiredHash, Guid.NewGuid(), _clock.UtcNow.AddMinutes(-5), _clock.UtcNow.AddDays(-7));
        dbContext.RefreshTokens.Add(expiredToken);
        await dbContext.SaveChangesAsync();

        var result = await handler.HandleAsync(new RefreshTokenCommand(expiredTokenRaw));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.RefreshTokenExpired");
    }

    [Fact]
    public async Task LogoutHandler_ShouldRevokeSpecificRefreshTokenAsync()
    {
        using var dbContext = CreateDbContext();
        var keyProvider = new JwtKeyProvider(Options.Create(_jwtOptions));
        var tokenService = new TokenService(keyProvider, _clock, Options.Create(_jwtOptions));
        var handler = new LogoutHandler(dbContext, tokenService, _clock, NullLogger<LogoutHandler>.Instance);

        var rawToken = "logout-test-token";
        var hash = tokenService.HashRefreshToken(rawToken);
        var token = RefreshToken.Create(Guid.NewGuid(), Guid.NewGuid(), hash, Guid.NewGuid(), _clock.UtcNow.AddDays(7), _clock.UtcNow);
        dbContext.RefreshTokens.Add(token);
        await dbContext.SaveChangesAsync();

        var result = await handler.HandleAsync(new LogoutCommand(rawToken));

        result.IsSuccess.Should().BeTrue();
        var inDb = await dbContext.RefreshTokens.FirstAsync(t => t.Id == token.Id);
        inDb.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task LogoutAllHandler_ShouldRevokeAllUserTokensAndRotateSecurityStampAsync()
    {
        using var dbContext = CreateDbContext();
        var handler = new LogoutAllHandler(dbContext, _clock, NullLogger<LogoutAllHandler>.Instance);

        var user = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Tarek", _clock.UtcNow);
        var originalStamp = user.SecurityStamp;
        dbContext.Users.Add(user);

        var token1 = RefreshToken.Create(Guid.NewGuid(), user.Id, "hash1", Guid.NewGuid(), _clock.UtcNow.AddDays(7), _clock.UtcNow);
        var token2 = RefreshToken.Create(Guid.NewGuid(), user.Id, "hash2", Guid.NewGuid(), _clock.UtcNow.AddDays(7), _clock.UtcNow);
        var tokenOther = RefreshToken.Create(Guid.NewGuid(), Guid.NewGuid(), "hash3", Guid.NewGuid(), _clock.UtcNow.AddDays(7), _clock.UtcNow);

        dbContext.RefreshTokens.AddRange(token1, token2, tokenOther);
        await dbContext.SaveChangesAsync();

        var result = await handler.HandleAsync(new LogoutAllCommand(user.Id));

        result.IsSuccess.Should().BeTrue();

        var userTokens = await dbContext.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync();
        userTokens.Should().OnlyContain(t => t.IsRevoked);

        var otherTokenInDb = await dbContext.RefreshTokens.FirstAsync(t => t.Id == tokenOther.Id);
        otherTokenInDb.IsRevoked.Should().BeFalse();

        var userInDb = await dbContext.Users.FirstAsync(u => u.Id == user.Id);
        userInDb.SecurityStamp.Should().NotBe(originalStamp);
    }

    [Fact]
    public void RefreshTokenValidator_ShouldValidateCorrectly()
    {
        var validator = new RefreshTokenValidator();
        validator.Validate(new RefreshTokenCommand("valid_token")).IsValid.Should().BeTrue();
        validator.Validate(new RefreshTokenCommand("")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void LogoutValidator_ShouldValidateCorrectly()
    {
        var validator = new LogoutValidator();
        validator.Validate(new LogoutCommand("valid_token")).IsValid.Should().BeTrue();
        validator.Validate(new LogoutCommand("")).IsValid.Should().BeFalse();
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
}
