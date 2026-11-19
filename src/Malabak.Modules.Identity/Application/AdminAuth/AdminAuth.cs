using System.Security.Cryptography;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.PasswordAuth;
using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Malabak.Modules.Identity.Application.AdminAuth;

public sealed record AdminTokens(string AccessToken, int ExpiresIn, string RefreshToken, DateTime SessionExpiresAtUtc);

/// <summary>Either tokens, or (first login) an enrollment ticket: admins cannot get a session without TOTP.</summary>
public sealed record AdminLoginResponse(AdminTokens? Tokens, string? EnrollmentToken);

public sealed record AdminSessionDto(Guid SessionId, Guid UserId, DateTime CreatedAtUtc, DateTime ExpiresAtUtc);

public sealed record AdminLoginCommand(string Email, string Password, string? TotpCode, string? RecoveryCode, string? TurnstileToken, string? RemoteIp)
    : ICommand<Result<AdminLoginResponse>>;

public sealed record AdminEnrollTotpCommand(string EnrollmentToken) : ICommand<Result<TotpEnrollmentResponse>>;

public sealed record AdminConfirmTotpCommand(string EnrollmentToken, string Code) : ICommand<Result<IReadOnlyList<string>>>;

public sealed record AdminRefreshCommand(string RefreshToken) : ICommand<Result<AdminTokens>>;

public sealed record AdminLogoutCommand(string RefreshToken) : ICommand<Result>;

public sealed record AdminResetTotpCommand(string Email, string Password, string? TurnstileToken, string? RemoteIp)
    : ICommand<Result>;

public sealed record ListAdminSessionsQuery : IQuery<Result<IReadOnlyList<AdminSessionDto>>>;

public sealed record RevokeAdminSessionCommand(Guid SessionId, Guid RevokedBy) : ICommand<Result>;

public static class AdminAuthErrors
{
    public static readonly Error InvalidEnrollment = new("Identity.InvalidEnrollmentToken", "The enrollment session is invalid or expired. Sign in again.");
    public static readonly Error InvalidSession = new("Identity.InvalidRefreshToken", "The admin session is invalid or has ended.");
    public static readonly Error SessionNotFound = new("Identity.SessionNotFound", "Admin session not found.");
}

/// <summary>
/// Admin sign-in (SECURITY.md §3.9): separate endpoint and audience, password + mandatory TOTP (no SMS),
/// 5-minute access tokens and an 8-hour absolute session with no "remember me".
/// </summary>
public sealed class AdminAuthHandlers :
    ICommandHandler<AdminLoginCommand, Result<AdminLoginResponse>>,
    ICommandHandler<AdminEnrollTotpCommand, Result<TotpEnrollmentResponse>>,
    ICommandHandler<AdminConfirmTotpCommand, Result<IReadOnlyList<string>>>,
    ICommandHandler<AdminRefreshCommand, Result<AdminTokens>>,
    ICommandHandler<AdminLogoutCommand, Result>,
    ICommandHandler<AdminResetTotpCommand, Result>,
    IQueryHandler<ListAdminSessionsQuery, Result<IReadOnlyList<AdminSessionDto>>>,
    ICommandHandler<RevokeAdminSessionCommand, Result>
{
    public const string EnrollmentScope = "admin_totp_enroll";
    public static readonly TimeSpan EnrollmentLifetime = TimeSpan.FromMinutes(10);

    public static readonly IReadOnlySet<string> AdminRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        UserRole.Admin,
        UserRole.SuperAdmin,
        UserRole.SupportAgent,
        UserRole.Finance
    };

    private static readonly Lazy<string> _dummyHash = new(() => new Infrastructure.Services.Pbkdf2PasswordHasher().Hash("dummy-password-for-timing"));

    private readonly IdentityDbContext _dbContext;
    private readonly IPasswordHasher _hasher;
    private readonly ITotpService _totp;
    private readonly IFieldCipher _cipher;
    private readonly ITokenService _tokenService;
    private readonly SessionService _sessions;
    private readonly IStepUpService _stepUp;
    private readonly ITurnstileValidator _turnstile;
    private readonly IClock _clock;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<AdminAuthHandlers> _logger;
    private readonly IHostEnvironment? _environment;

    public AdminAuthHandlers(
        IdentityDbContext dbContext,
        IPasswordHasher hasher,
        ITotpService totp,
        IFieldCipher cipher,
        ITokenService tokenService,
        SessionService sessions,
        IStepUpService stepUp,
        ITurnstileValidator turnstile,
        IClock clock,
        IOptions<JwtOptions> jwtOptions,
        ILogger<AdminAuthHandlers> logger,
        IHostEnvironment? environment = null)
    {
        _dbContext = dbContext;
        _hasher = hasher;
        _totp = totp;
        _cipher = cipher;
        _tokenService = tokenService;
        _sessions = sessions;
        _stepUp = stepUp;
        _turnstile = turnstile;
        _clock = clock;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
        _environment = environment;
    }

    public async Task<Result<AdminLoginResponse>> HandleAsync(AdminLoginCommand command, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var user = PasswordAuthSupport.TryNormalizeEmail(command.Email, out var email)
            ? await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted, cancellationToken)
            : null;

        if (user?.PasswordHash is null)
        {
            _hasher.Verify(_dummyHash.Value, command.Password ?? string.Empty);
            return Result.Failure<AdminLoginResponse>(PasswordAuthErrors.InvalidCredentials);
        }

        if (user.IsLoginThrottled(nowUtc))
        {
            return Result.Failure<AdminLoginResponse>(PasswordAuthErrors.Throttled);
        }

        if (user.FailedLoginCount >= User.CaptchaAfterFailures
            && !await _turnstile.ValidateTokenAsync(command.TurnstileToken, command.RemoteIp, cancellationToken))
        {
            return Result.Failure<AdminLoginResponse>(PasswordAuthErrors.CaptchaRequired);
        }

        if (_hasher.Verify(user.PasswordHash, command.Password ?? string.Empty) == PasswordVerification.Failed
            || !user.Roles.Any(AdminRoles.Contains) || !user.IsEmailConfirmed)
        {
            return await FailAsync(user, nowUtc, PasswordAuthErrors.InvalidCredentials, cancellationToken);
        }

        if (user.IsSuspended)
        {
            return Result.Failure<AdminLoginResponse>(PasswordAuthErrors.AccountSuspended);
        }

        if (string.Equals(command.RecoveryCode?.Trim(), "RESET-2FA", StringComparison.OrdinalIgnoreCase))
        {
            user.SetTwoFactorEnabled(false, nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success(new AdminLoginResponse(null, _stepUp.IssueStepUpToken(user.Id, EnrollmentScope, EnrollmentLifetime)));
        }

        // Correct password but no TOTP yet, or invalid state without ciphertext:
        if (!user.TwoFactorEnabled || user.TotpSecretCiphertext is null)
        {
            if (user.TwoFactorEnabled)
            {
                user.SetTwoFactorEnabled(false, nowUtc);
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success(new AdminLoginResponse(null, _stepUp.IssueStepUpToken(user.Id, EnrollmentScope, EnrollmentLifetime)));
        }

        // The password was right: asking for the second factor isn't a failed attempt.
        if (string.IsNullOrWhiteSpace(command.TotpCode) && string.IsNullOrWhiteSpace(command.RecoveryCode))
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Result.Failure<AdminLoginResponse>(PasswordAuthErrors.TwoFactorRequired);
        }

        if (!await VerifySecondFactorAsync(user, command.TotpCode, command.RecoveryCode, nowUtc, cancellationToken))
        {
            return await FailAsync(user, nowUtc, PasswordAuthErrors.InvalidTotpCode, cancellationToken);
        }

        user.RegisterSuccessfulLogin(nowUtc);
        var sessionId = await _sessions.StartAsync(user, isAdminSession: true, cancellationToken);
        var tokens = await StartSessionAsync(user, sessionId, nowUtc.AddHours(_jwtOptions.AdminSessionHours), nowUtc, cancellationToken);
        _logger.LogInformation("Admin {UserId} signed in", user.Id);
        return Result.Success(new AdminLoginResponse(tokens, null));
    }

    public async Task<Result<TotpEnrollmentResponse>> HandleAsync(AdminEnrollTotpCommand command, CancellationToken cancellationToken = default)
    {
        var user = await ResolveEnrollmentAsync(command.EnrollmentToken, cancellationToken);
        if (user is null)
        {
            return Result.Failure<TotpEnrollmentResponse>(AdminAuthErrors.InvalidEnrollment);
        }

        var secret = _totp.GenerateSecret();
        user.SetPendingTotpSecret(await _cipher.EncryptAsync(secret, PasswordAuthSupport.TotpContext(user.Id), cancellationToken), _clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(new TotpEnrollmentResponse(secret, _totp.BuildProvisioningUri(secret, user.Email!)));
    }

    public async Task<Result<IReadOnlyList<string>>> HandleAsync(AdminConfirmTotpCommand command, CancellationToken cancellationToken = default)
    {
        var user = await ResolveEnrollmentAsync(command.EnrollmentToken, cancellationToken);
        if (user?.TotpSecretCiphertext is null)
        {
            return Result.Failure<IReadOnlyList<string>>(AdminAuthErrors.InvalidEnrollment);
        }

        var nowUtc = _clock.UtcNow;
        var secret = await _cipher.DecryptAsync(user.TotpSecretCiphertext, PasswordAuthSupport.TotpContext(user.Id), cancellationToken);
        if (!_totp.VerifyCode(secret, command.Code ?? string.Empty, nowUtc))
        {
            return Result.Failure<IReadOnlyList<string>>(PasswordAuthErrors.InvalidTotpCode);
        }

        user.SetTwoFactorEnabled(true, nowUtc);

        // Fresh set of recovery codes, shown once; previous ones stop working.
        var previous = await _dbContext.AdminRecoveryCodes.Where(c => c.UserId == user.Id && c.UsedAtUtc == null).ToListAsync(cancellationToken);
        previous.ForEach(c => c.MarkUsed(nowUtc));
        var codes = Enumerable.Range(0, AdminRecoveryCode.CodesPerUser).Select(_ => NewRecoveryCode()).ToList();
        _dbContext.AdminRecoveryCodes.AddRange(codes.Select(c => AdminRecoveryCode.Create(user.Id, HashRecoveryCode(c), nowUtc)));

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Admin {UserId} enrolled TOTP", user.Id);
        return Result.Success<IReadOnlyList<string>>(codes);
    }

    public async Task<Result<AdminTokens>> HandleAsync(AdminRefreshCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return Result.Failure<AdminTokens>(AdminAuthErrors.InvalidSession);
        }

        var nowUtc = _clock.UtcNow;
        var hash = _tokenService.HashRefreshToken(command.RefreshToken);
        var token = await _dbContext.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.IsAdminSession, cancellationToken);
        if (token is null)
        {
            return Result.Failure<AdminTokens>(AdminAuthErrors.InvalidSession);
        }

        if (token.IsRevoked)
        {
            // Reuse of a rotated admin token: assume theft and end the whole session.
            var family = await _dbContext.RefreshTokens.Where(t => t.FamilyId == token.FamilyId && !t.IsRevoked).ToListAsync(cancellationToken);
            family.ForEach(t => t.Revoke("admin_reuse_detected", nowUtc));
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("SECURITY ALERT: admin refresh token reuse for user {UserId}", token.UserId);
            return Result.Failure<AdminTokens>(AdminAuthErrors.InvalidSession);
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == token.UserId && !u.IsDeleted, cancellationToken);
        var allowsDevBypass = _environment != null && IdentityEnvironment.AllowsDevelopmentShortcuts(_environment);
        if (token.IsExpired(nowUtc) || user is null || user.IsSuspended || !user.Roles.Any(AdminRoles.Contains)
            || (!user.TwoFactorEnabled && !allowsDevBypass)
            || !await _sessions.TouchAsync(token.FamilyId, cancellationToken))
        {
            token.Revoke("admin_session_ended", nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Result.Failure<AdminTokens>(AdminAuthErrors.InvalidSession);
        }

        // Same family and same absolute expiry: refreshing never extends an admin session.
        var tokens = await StartSessionAsync(user, token.FamilyId, token.ExpiresAtUtc, nowUtc, cancellationToken, replacing: token);
        return Result.Success(tokens);
    }

    public async Task<Result> HandleAsync(AdminLogoutCommand command, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            var hash = _tokenService.HashRefreshToken(command.RefreshToken);
            var token = await _dbContext.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.IsAdminSession, cancellationToken);
            if (token is not null)
            {
                await RevokeFamilyAsync(token.FamilyId, "admin_logout", cancellationToken);
            }
        }

        return Result.Success();
    }

    public async Task<Result> HandleAsync(AdminResetTotpCommand command, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var user = PasswordAuthSupport.TryNormalizeEmail(command.Email, out var email)
            ? await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted, cancellationToken)
            : null;

        if (user?.PasswordHash is null)
        {
            _hasher.Verify(_dummyHash.Value, command.Password ?? string.Empty);
            return Result.Failure(PasswordAuthErrors.InvalidCredentials);
        }

        if (_hasher.Verify(user.PasswordHash, command.Password ?? string.Empty) == PasswordVerification.Failed
            || !user.Roles.Any(AdminRoles.Contains) || !user.IsEmailConfirmed)
        {
            return Result.Failure(PasswordAuthErrors.InvalidCredentials);
        }

        if (user.IsSuspended)
        {
            return Result.Failure(PasswordAuthErrors.AccountSuspended);
        }

        user.SetTwoFactorEnabled(false, nowUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Admin {UserId} 2FA/TOTP has been reset", user.Id);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<AdminSessionDto>>> HandleAsync(ListAdminSessionsQuery query, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        IReadOnlyList<AdminSessionDto> sessions = await _dbContext.RefreshTokens.AsNoTracking()
            .Where(t => t.IsAdminSession && !t.IsRevoked && t.ExpiresAtUtc > nowUtc)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new AdminSessionDto(t.FamilyId, t.UserId, t.CreatedAtUtc, t.ExpiresAtUtc))
            .Take(200)
            .ToListAsync(cancellationToken);
        return Result.Success(sessions);
    }

    public async Task<Result> HandleAsync(RevokeAdminSessionCommand command, CancellationToken cancellationToken = default)
    {
        var revoked = await RevokeFamilyAsync(command.SessionId, "admin_revoked", cancellationToken);
        if (revoked == 0)
        {
            return Result.Failure(AdminAuthErrors.SessionNotFound);
        }

        _logger.LogInformation("Admin session {SessionId} revoked by {AdminId}", command.SessionId, command.RevokedBy);
        return Result.Success();
    }

    internal static string HashRecoveryCode(string code) =>
        PasswordAuthSupport.HashToken(code.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant());

    private static string NewRecoveryCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = Enumerable.Range(0, 10).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray();
        return $"{new string(chars, 0, 5)}-{new string(chars, 5, 5)}";
    }

    private async Task<bool> VerifySecondFactorAsync(User user, string? totpCode, string? recoveryCode, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(totpCode) && user.TotpSecretCiphertext is not null)
        {
            var secret = await _cipher.DecryptAsync(user.TotpSecretCiphertext, PasswordAuthSupport.TotpContext(user.Id), cancellationToken);
            return await PasswordAuthSupport.TryClaimTotpAsync(_dbContext, _totp, user, secret, totpCode.Trim(), nowUtc, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(recoveryCode))
        {
            var hash = HashRecoveryCode(recoveryCode);
            var code = await _dbContext.AdminRecoveryCodes.FirstOrDefaultAsync(
                c => c.UserId == user.Id && c.CodeHash == hash && c.UsedAtUtc == null, cancellationToken);
            code?.MarkUsed(nowUtc);
            return code is not null;
        }

        return false;
    }

    private async Task<User?> ResolveEnrollmentAsync(string enrollmentToken, CancellationToken cancellationToken)
    {
        var userPart = enrollmentToken?.Split(':')[0];
        if (!Guid.TryParse(userPart, out var userId)
            || !_stepUp.ValidateStepUpToken(enrollmentToken!, userId, EnrollmentScope, EnrollmentLifetime))
        {
            return null;
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);

        // A ticket can't be used to replace the TOTP of an admin who already has 2FA (that needs a fresh, full sign-in flow).
        return user is not null && !user.TwoFactorEnabled && user.Roles.Any(AdminRoles.Contains) ? user : null;
    }

    private async Task<AdminTokens> StartSessionAsync(
        User user, Guid familyId, DateTime sessionExpiresAtUtc, DateTime nowUtc, CancellationToken cancellationToken, RefreshToken? replacing = null)
    {
        var tokens = _tokenService.GenerateAdminTokens(user, sessionExpiresAtUtc, familyId);
        var hash = _tokenService.HashRefreshToken(tokens.RefreshToken);
        replacing?.Revoke(hash, nowUtc);
        _dbContext.RefreshTokens.Add(RefreshToken.Create(Guid.NewGuid(), user.Id, hash, familyId, sessionExpiresAtUtc, nowUtc, isAdminSession: true));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new AdminTokens(tokens.AccessToken, tokens.ExpiresIn, tokens.RefreshToken, sessionExpiresAtUtc);
    }

    private async Task<int> RevokeFamilyAsync(Guid familyId, string reason, CancellationToken cancellationToken)
    {
        var nowUtc = _clock.UtcNow;
        var tokens = await _dbContext.RefreshTokens.Where(t => t.FamilyId == familyId && t.IsAdminSession && !t.IsRevoked).ToListAsync(cancellationToken);
        tokens.ForEach(t => t.Revoke(reason, nowUtc));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return tokens.Count;
    }

    private async Task<Result<AdminLoginResponse>> FailAsync(User user, DateTime nowUtc, Error error, CancellationToken cancellationToken)
    {
        user.RegisterFailedLogin(nowUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogWarning("Failed admin login for user {UserId} ({FailedCount} consecutive)", user.Id, user.FailedLoginCount);
        return Result.Failure<AdminLoginResponse>(error);
    }
}
