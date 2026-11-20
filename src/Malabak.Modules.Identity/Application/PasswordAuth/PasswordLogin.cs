using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.PasswordAuth;

/// <param name="Email">The email or the mobile number the account was registered with.</param>
public sealed record PasswordLoginCommand(string Email, string Password, string? TotpCode, string? TurnstileToken, string? RemoteIp)
    : ICommand<Result<PasswordLoginResponse>>;

public sealed class PasswordLoginHandler : ICommandHandler<PasswordLoginCommand, Result<PasswordLoginResponse>>
{
    // Verified against when the email is unknown so both paths cost the same PBKDF2 work (no timing oracle).
    private static readonly Lazy<string> _dummyHash = new(() => new Infrastructure.Services.Pbkdf2PasswordHasher().Hash("dummy-password-for-timing"));

    private readonly IdentityDbContext _dbContext;
    private readonly IPasswordHasher _hasher;
    private readonly IPhoneNumberNormalizer _phoneNormalizer;
    private readonly ITurnstileValidator _turnstile;
    private readonly ITotpService _totp;
    private readonly IFieldCipher _cipher;
    private readonly ITokenService _tokenService;
    private readonly SessionService _sessions;
    private readonly IClock _clock;
    private readonly ILogger<PasswordLoginHandler> _logger;

    public PasswordLoginHandler(
        IdentityDbContext dbContext,
        IPasswordHasher hasher,
        IPhoneNumberNormalizer phoneNormalizer,
        ITurnstileValidator turnstile,
        ITotpService totp,
        IFieldCipher cipher,
        ITokenService tokenService,
        SessionService sessions,
        IClock clock,
        ILogger<PasswordLoginHandler> logger)
    {
        _dbContext = dbContext;
        _hasher = hasher;
        _phoneNormalizer = phoneNormalizer;
        _turnstile = turnstile;
        _totp = totp;
        _cipher = cipher;
        _tokenService = tokenService;
        _sessions = sessions;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<PasswordLoginResponse>> HandleAsync(PasswordLoginCommand command, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        User? user = null;
        var identifier = command.Email ?? string.Empty;
        if (identifier.Contains('@'))
        {
            if (PasswordAuthSupport.TryNormalizeEmail(identifier, out var email))
            {
                user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted, cancellationToken);
            }
        }
        else if (_phoneNormalizer.Normalize(identifier) is { IsSuccess: true } phone)
        {
            user = await _dbContext.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone.Value && !u.IsDeleted, cancellationToken);
        }

        // Unknown or malformed identifiers take the same path and cost as a wrong password (no account probing).
        if (user?.PasswordHash is null)
        {
            _hasher.Verify(_dummyHash.Value, command.Password ?? string.Empty);
            return Result.Failure<PasswordLoginResponse>(PasswordAuthErrors.InvalidCredentials);
        }

        if (user.IsLoginThrottled(nowUtc))
        {
            return Result.Failure<PasswordLoginResponse>(PasswordAuthErrors.Throttled);
        }

        if (user.FailedLoginCount >= User.CaptchaAfterFailures
            && !await _turnstile.ValidateTokenAsync(command.TurnstileToken, command.RemoteIp, cancellationToken))
        {
            return Result.Failure<PasswordLoginResponse>(PasswordAuthErrors.CaptchaRequired);
        }

        var verification = _hasher.Verify(user.PasswordHash, command.Password ?? string.Empty);
        if (verification == PasswordVerification.Failed || !user.Roles.Any(PasswordAuthSupport.PasswordRoles.Contains))
        {
            return await FailAsync(user, nowUtc, PasswordAuthErrors.InvalidCredentials, cancellationToken);
        }

        // Only revealed after the password was proven, so suspension can't be used to probe accounts.
        if (user.IsSuspended)
        {
            return Result.Failure<PasswordLoginResponse>(PasswordAuthErrors.AccountSuspended);
        }

        if (!user.IsAccountActivated)
        {
            return Result.Failure<PasswordLoginResponse>(PasswordAuthErrors.EmailNotVerified);
        }

        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrWhiteSpace(command.TotpCode))
            {
                return Result.Failure<PasswordLoginResponse>(PasswordAuthErrors.TwoFactorRequired);
            }

            var secret = await _cipher.DecryptAsync(user.TotpSecretCiphertext!, PasswordAuthSupport.TotpContext(user.Id), cancellationToken);
            if (!await PasswordAuthSupport.TryClaimTotpAsync(_dbContext, _totp, user, secret, command.TotpCode.Trim(), nowUtc, cancellationToken))
            {
                return await FailAsync(user, nowUtc, PasswordAuthErrors.InvalidTotpCode, cancellationToken);
            }
        }

        if (verification == PasswordVerification.SuccessRehashNeeded)
        {
            user.SetPassword(_hasher.Hash(command.Password!), nowUtc);
        }

        user.RegisterSuccessfulLogin(nowUtc);
        _logger.LogInformation("User {UserId} signed in with password", user.Id);
        var sessionId = await _sessions.StartAsync(user, isAdminSession: false, cancellationToken);
        return Result.Success(await PasswordAuthSupport.IssueTokensAsync(_dbContext, _tokenService, user, sessionId, nowUtc, cancellationToken));
    }

    private async Task<Result<PasswordLoginResponse>> FailAsync(User user, DateTime nowUtc, Error error, CancellationToken cancellationToken)
    {
        user.RegisterFailedLogin(nowUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogWarning("Failed password login for user {UserId} ({FailedCount} consecutive)", user.Id, user.FailedLoginCount);
        return Result.Failure<PasswordLoginResponse>(error);
    }
}
