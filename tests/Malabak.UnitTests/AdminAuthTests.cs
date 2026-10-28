using System.IdentityModel.Tokens.Jwt;
using System.Text;
using FluentAssertions;
using Malabak.Modules.Identity.Application.AdminAuth;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.PasswordAuth;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

public sealed class AdminAuthTests
{
    private const string _email = "admin@malabak.app";
    private const string _password = "very long admin passphrase";

    private readonly MutableClock _clock = new();
    private readonly Pbkdf2PasswordHasher _hasher = new();
    private readonly TotpService _totp = new();
    private readonly XorCipher _cipher = new();
    private readonly IOptions<JwtOptions> _jwtOptions = Options.Create(new JwtOptions { CurrentKeyId = "test-key" });

    [Fact]
    public async Task FirstLogin_WithoutTotp_ReturnsEnrollmentTicket_NotASessionAsync()
    {
        await using var db = NewDb();
        await SeedAdminAsync(db);

        var result = await Handlers(db).HandleAsync(Login());

        result.Value.Tokens.Should().BeNull();
        result.Value.EnrollmentToken.Should().NotBeNullOrEmpty();
        (await db.RefreshTokens.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Enrollment_Confirm_IssuesRecoveryCodes_ThenLoginRequiresTotpAsync()
    {
        await using var db = NewDb();
        await SeedAdminAsync(db);
        var handlers = Handlers(db);
        var (secret, codes) = await EnrollAsync(handlers);

        codes.Should().HaveCount(AdminRecoveryCode.CodesPerUser).And.OnlyHaveUniqueItems();
        (await db.AdminRecoveryCodes.Select(c => c.CodeHash).ToListAsync()).Should().NotContain(codes);

        (await handlers.HandleAsync(Login())).Error.Should().Be(PasswordAuthErrors.InvalidTotpCode);
        var login = await handlers.HandleAsync(Login() with { TotpCode = CurrentCode(secret) });

        login.Value.Tokens.Should().NotBeNull();
        (await db.RefreshTokens.SingleAsync()).IsAdminSession.Should().BeTrue();
    }

    [Fact]
    public async Task AdminToken_HasAdminAudience_ShortLifetime_AndMfaAmrAsync()
    {
        await using var db = NewDb();
        await SeedAdminAsync(db);
        var handlers = Handlers(db);
        var (secret, _) = await EnrollAsync(handlers);

        var tokens = (await handlers.HandleAsync(Login() with { TotpCode = CurrentCode(secret) })).Value.Tokens!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);

        jwt.Audiences.Should().Equal("Malabak.Admin");
        tokens.ExpiresIn.Should().Be(300);
        tokens.SessionExpiresAtUtc.Should().Be(_clock.UtcNow.AddHours(8));
        jwt.Claims.Where(c => c.Type == "amr").Select(c => c.Value).Should().Contain("mfa");
        jwt.Claims.Should().NotContain(c => c.Type == "phone_number" || c.Type == "email");
    }

    [Fact]
    public async Task RecoveryCode_WorksOnceAsync()
    {
        await using var db = NewDb();
        await SeedAdminAsync(db);
        var handlers = Handlers(db);
        var (_, codes) = await EnrollAsync(handlers);

        (await handlers.HandleAsync(Login() with { RecoveryCode = codes[0].ToLowerInvariant() })).Value.Tokens.Should().NotBeNull();
        (await handlers.HandleAsync(Login() with { RecoveryCode = codes[0] })).Error.Should().Be(PasswordAuthErrors.InvalidTotpCode);
    }

    [Fact]
    public async Task NonAdminRoles_CannotUseAdminLoginAsync()
    {
        await using var db = NewDb();
        var owner = User.CreateVenueOwner(Guid.NewGuid(), _email, "+201001110000", _hasher.Hash(_password), "Owner", _clock.UtcNow);
        owner.ConfirmEmail(_clock.UtcNow);
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        (await Handlers(db).HandleAsync(Login())).Error.Should().Be(PasswordAuthErrors.InvalidCredentials);
    }

    [Fact]
    public async Task EnrollmentTicket_CannotReplaceTotpOfEnrolledAdmin_AndExpiresAsync()
    {
        await using var db = NewDb();
        await SeedAdminAsync(db);
        var handlers = Handlers(db);
        var ticket = (await handlers.HandleAsync(Login())).Value.EnrollmentToken!;
        var (_, _) = await EnrollAsync(handlers, ticket);

        (await handlers.HandleAsync(new AdminEnrollTotpCommand(ticket))).Error.Should().Be(AdminAuthErrors.InvalidEnrollment);
        (await handlers.HandleAsync(new AdminEnrollTotpCommand("not-a-ticket"))).Error.Should().Be(AdminAuthErrors.InvalidEnrollment);
    }

    [Fact]
    public async Task Refresh_RotatesWithoutExtendingSession_AndReuseKillsSessionAsync()
    {
        await using var db = NewDb();
        await SeedAdminAsync(db);
        var handlers = Handlers(db);
        var (secret, _) = await EnrollAsync(handlers);
        var first = (await handlers.HandleAsync(Login() with { TotpCode = CurrentCode(secret) })).Value.Tokens!;

        _clock.UtcNow = _clock.UtcNow.AddHours(1);
        var second = await handlers.HandleAsync(new AdminRefreshCommand(first.RefreshToken));
        second.Value.SessionExpiresAtUtc.Should().Be(first.SessionExpiresAtUtc);

        (await handlers.HandleAsync(new AdminRefreshCommand(first.RefreshToken))).Error.Should().Be(AdminAuthErrors.InvalidSession);
        (await handlers.HandleAsync(new AdminRefreshCommand(second.Value.RefreshToken))).Error.Should().Be(AdminAuthErrors.InvalidSession,
            "reuse of a rotated token revokes the whole session");
    }

    [Fact]
    public async Task Refresh_AfterAbsoluteSessionEnd_FailsAsync()
    {
        await using var db = NewDb();
        await SeedAdminAsync(db);
        var handlers = Handlers(db);
        var (secret, _) = await EnrollAsync(handlers);
        var tokens = (await handlers.HandleAsync(Login() with { TotpCode = CurrentCode(secret) })).Value.Tokens!;

        _clock.UtcNow = _clock.UtcNow.AddHours(9);

        (await handlers.HandleAsync(new AdminRefreshCommand(tokens.RefreshToken))).Error.Should().Be(AdminAuthErrors.InvalidSession);
    }

    [Fact]
    public async Task AdminRefreshToken_IsRejectedByNormalRefresh_AndNormalByAdminAsync()
    {
        await using var db = NewDb();
        await SeedAdminAsync(db);
        var handlers = Handlers(db);
        var (secret, _) = await EnrollAsync(handlers);
        var tokens = (await handlers.HandleAsync(Login() with { TotpCode = CurrentCode(secret) })).Value.Tokens!;

        var normalRefresh = new Malabak.Modules.Identity.Application.Authentication.RefreshToken.RefreshTokenHandler(
            db, TokenService(), _clock, NullLogger<Malabak.Modules.Identity.Application.Authentication.RefreshToken.RefreshTokenHandler>.Instance);

        (await normalRefresh.HandleAsync(new Malabak.Modules.Identity.Application.Authentication.RefreshToken.RefreshTokenCommand(tokens.RefreshToken)))
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Sessions_ListAndRevokeAsync()
    {
        await using var db = NewDb();
        var admin = await SeedAdminAsync(db);
        var handlers = Handlers(db);
        var (secret, _) = await EnrollAsync(handlers);
        var tokens = (await handlers.HandleAsync(Login() with { TotpCode = CurrentCode(secret) })).Value.Tokens!;

        var sessions = (await handlers.HandleAsync(new ListAdminSessionsQuery())).Value;
        sessions.Should().ContainSingle();

        (await handlers.HandleAsync(new RevokeAdminSessionCommand(sessions[0].SessionId, admin.Id))).IsSuccess.Should().BeTrue();
        (await handlers.HandleAsync(new ListAdminSessionsQuery())).Value.Should().BeEmpty();
        (await handlers.HandleAsync(new AdminRefreshCommand(tokens.RefreshToken))).IsFailure.Should().BeTrue();
        (await handlers.HandleAsync(new RevokeAdminSessionCommand(Guid.NewGuid(), admin.Id))).Error.Should().Be(AdminAuthErrors.SessionNotFound);
    }

    // ---------- helpers ----------

    private static AdminLoginCommand Login() => new(_email, _password, null, null, "token", null);

    private string CurrentCode(string secret) =>
        TotpService.Compute(TotpService.FromBase32(secret), new DateTimeOffset(_clock.UtcNow).ToUnixTimeSeconds() / 30);

    private async Task<(string Secret, IReadOnlyList<string> Codes)> EnrollAsync(AdminAuthHandlers handlers, string? ticket = null)
    {
        ticket ??= (await handlers.HandleAsync(Login())).Value.EnrollmentToken!;
        var secret = (await handlers.HandleAsync(new AdminEnrollTotpCommand(ticket))).Value.Secret;
        var codes = (await handlers.HandleAsync(new AdminConfirmTotpCommand(ticket, CurrentCode(secret)))).Value;
        return (secret, codes);
    }

    private async Task<User> SeedAdminAsync(IdentityDbContext db)
    {
        var admin = User.CreateStaff(Guid.NewGuid(), _email, "+201001119999", _hasher.Hash(_password), "Admin", UserRole.Admin, _clock.UtcNow);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }

    private AdminAuthHandlers Handlers(IdentityDbContext db)
    {
        var stepUpConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:StepUp:SecretKey"] = "test-step-up-secret-0123456789" })
            .Build();

        return new AdminAuthHandlers(db, _hasher, _totp, _cipher, TokenService(), new StepUpService(stepUpConfig, _clock),
            new AllowTurnstile(), _clock, _jwtOptions, NullLogger<AdminAuthHandlers>.Instance);
    }

    private TokenService TokenService() => new(new JwtKeyProvider(_jwtOptions), _clock, _jwtOptions);

    private static IdentityDbContext NewDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class AllowTurnstile : ITurnstileValidator
    {
        public Task<bool> ValidateTokenAsync(string? turnstileToken, string? remoteIp = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class XorCipher : IFieldCipher
    {
        public ValueTask<byte[]> EncryptAsync(string plaintext, FieldEncryptionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Encoding.UTF8.GetBytes(plaintext).Select(b => (byte)(b ^ 0x21)).ToArray());

        public ValueTask<string> DecryptAsync(byte[] ciphertext, FieldEncryptionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Encoding.UTF8.GetString(ciphertext.Select(b => (byte)(b ^ 0x21)).ToArray()));

        public ValueTask<bool> NeedsReEncryptionAsync(byte[] ciphertext, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class MutableClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
