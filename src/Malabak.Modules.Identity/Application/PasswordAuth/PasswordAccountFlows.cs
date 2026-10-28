using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.PasswordAuth;

public sealed record RegisterVenueOwnerCommand(string Email, string PhoneNumber, string FullName, string Password) : ICommand<Result>;

public sealed record VerifyEmailCommand(string Token) : ICommand<Result>;

public sealed record ResendVerificationCommand(string Email) : ICommand<Result>;

public sealed record ForgotPasswordCommand(string Email) : ICommand<Result>;

public sealed record ResetPasswordCommand(string Token, string NewPassword) : ICommand<Result>;

public sealed record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword) : ICommand<Result>;

public sealed record EnrollTotpCommand(Guid UserId) : ICommand<Result<TotpEnrollmentResponse>>;

public sealed record ConfirmTotpCommand(Guid UserId, string Code) : ICommand<Result>;

/// <summary>
/// Account flows for password users (venue owners and staff). Request-type flows (resend, forgot) always
/// succeed so they can't be used to discover which emails have accounts.
/// </summary>
public sealed class PasswordAccountHandlers :
    ICommandHandler<RegisterVenueOwnerCommand, Result>,
    ICommandHandler<VerifyEmailCommand, Result>,
    ICommandHandler<ResendVerificationCommand, Result>,
    ICommandHandler<ForgotPasswordCommand, Result>,
    ICommandHandler<ResetPasswordCommand, Result>,
    ICommandHandler<ChangePasswordCommand, Result>,
    ICommandHandler<EnrollTotpCommand, Result<TotpEnrollmentResponse>>,
    ICommandHandler<ConfirmTotpCommand, Result>
{
    private static readonly TimeSpan _resendCooldown = TimeSpan.FromSeconds(60);

    private readonly IdentityDbContext _dbContext;
    private readonly IPasswordHasher _hasher;
    private readonly IPasswordPolicy _policy;
    private readonly IPhoneNumberNormalizer _phoneNormalizer;
    private readonly IEmailSender _emailSender;
    private readonly ITotpService _totp;
    private readonly IFieldCipher _cipher;
    private readonly IClock _clock;
    private readonly ILogger<PasswordAccountHandlers> _logger;
    private readonly string _dashboardUrl;

    public PasswordAccountHandlers(
        IdentityDbContext dbContext,
        IPasswordHasher hasher,
        IPasswordPolicy policy,
        IPhoneNumberNormalizer phoneNormalizer,
        IEmailSender emailSender,
        ITotpService totp,
        IFieldCipher cipher,
        IClock clock,
        IConfiguration configuration,
        ILogger<PasswordAccountHandlers> logger)
    {
        _dbContext = dbContext;
        _hasher = hasher;
        _policy = policy;
        _phoneNormalizer = phoneNormalizer;
        _emailSender = emailSender;
        _totp = totp;
        _cipher = cipher;
        _clock = clock;
        _logger = logger;
        _dashboardUrl = (configuration["App:VenueDashboardUrl"] ?? "http://localhost:3000").TrimEnd('/');
    }

    public async Task<Result> HandleAsync(RegisterVenueOwnerCommand command, CancellationToken cancellationToken = default)
    {
        if (!PasswordAuthSupport.TryNormalizeEmail(command.Email, out var email))
        {
            return Result.Failure(PasswordAuthErrors.InvalidEmail);
        }

        var phone = _phoneNormalizer.Normalize(command.PhoneNumber);
        if (phone.IsFailure)
        {
            return Result.Failure(phone.Error);
        }

        if (string.IsNullOrWhiteSpace(command.FullName) || command.FullName.Trim().Length > 200)
        {
            return Result.Failure(PasswordAuthErrors.RegistrationFailed);
        }

        var violations = await _policy.ValidateAsync(command.Password, email, cancellationToken);
        if (violations.Count > 0)
        {
            return Result.Failure(PasswordAuthErrors.WeakPassword(violations));
        }

        if (await _dbContext.Users.AnyAsync(u => u.Email == email || u.PhoneNumber == phone.Value, cancellationToken))
        {
            return Result.Failure(PasswordAuthErrors.RegistrationFailed);
        }

        var nowUtc = _clock.UtcNow;
        var user = User.CreateVenueOwner(Guid.CreateVersion7(), email, phone.Value, _hasher.Hash(command.Password), command.FullName, nowUtc);
        _dbContext.Users.Add(user);
        var raw = IssueToken(user.Id, UserActionTokenPurpose.EmailVerification, nowUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await SendSafelyAsync(email, "Verify your Malabak email", $"Confirm your email: {_dashboardUrl}/verify-email?token={raw}", cancellationToken);
        _logger.LogInformation("Venue owner {UserId} registered; verification email sent", user.Id);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(VerifyEmailCommand command, CancellationToken cancellationToken = default)
    {
        var (token, user) = await ConsumeTokenAsync(command.Token, UserActionTokenPurpose.EmailVerification, cancellationToken);
        if (token is null || user is null)
        {
            return Result.Failure(PasswordAuthErrors.InvalidToken);
        }

        user.ConfirmEmail(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(ResendVerificationCommand command, CancellationToken cancellationToken = default)
    {
        if (PasswordAuthSupport.TryNormalizeEmail(command.Email, out var email))
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted && !u.IsEmailConfirmed, cancellationToken);
            if (user is not null && !await RecentlyIssuedAsync(user.Id, UserActionTokenPurpose.EmailVerification, cancellationToken))
            {
                var raw = IssueToken(user.Id, UserActionTokenPurpose.EmailVerification, _clock.UtcNow);
                await _dbContext.SaveChangesAsync(cancellationToken);
                await SendSafelyAsync(email, "Verify your Malabak email", $"Confirm your email: {_dashboardUrl}/verify-email?token={raw}", cancellationToken);
            }
        }

        return Result.Success();
    }

    public async Task<Result> HandleAsync(ForgotPasswordCommand command, CancellationToken cancellationToken = default)
    {
        if (PasswordAuthSupport.TryNormalizeEmail(command.Email, out var email))
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted && u.PasswordHash != null, cancellationToken);
            if (user is not null && !await RecentlyIssuedAsync(user.Id, UserActionTokenPurpose.PasswordReset, cancellationToken))
            {
                var nowUtc = _clock.UtcNow;
                await InvalidateOpenTokensAsync(user.Id, UserActionTokenPurpose.PasswordReset, nowUtc, cancellationToken);
                var raw = IssueToken(user.Id, UserActionTokenPurpose.PasswordReset, nowUtc);
                await _dbContext.SaveChangesAsync(cancellationToken);
                await SendSafelyAsync(email, "Reset your Malabak password",
                    $"Reset your password (valid 30 minutes): {_dashboardUrl}/reset-password?token={raw}", cancellationToken);
            }
        }

        return Result.Success();
    }

    public async Task<Result> HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken = default)
    {
        var (token, user) = await ConsumeTokenAsync(command.Token, UserActionTokenPurpose.PasswordReset, cancellationToken);
        if (token is null || user is null)
        {
            return Result.Failure(PasswordAuthErrors.InvalidToken);
        }

        var violations = await _policy.ValidateAsync(command.NewPassword, user.Email, cancellationToken);
        if (violations.Count > 0)
        {
            return Result.Failure(PasswordAuthErrors.WeakPassword(violations));
        }

        await ApplyNewPasswordAsync(user, command.NewPassword, "password_reset", cancellationToken);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);
        if (user?.PasswordHash is null || _hasher.Verify(user.PasswordHash, command.CurrentPassword ?? string.Empty) == PasswordVerification.Failed)
        {
            return Result.Failure(PasswordAuthErrors.InvalidCredentials);
        }

        var violations = await _policy.ValidateAsync(command.NewPassword, user.Email, cancellationToken);
        if (violations.Count > 0)
        {
            return Result.Failure(PasswordAuthErrors.WeakPassword(violations));
        }

        await ApplyNewPasswordAsync(user, command.NewPassword, "password_changed", cancellationToken);
        return Result.Success();
    }

    public async Task<Result<TotpEnrollmentResponse>> HandleAsync(EnrollTotpCommand command, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);
        if (user?.Email is null)
        {
            return Result.Failure<TotpEnrollmentResponse>(PasswordAuthErrors.InvalidCredentials);
        }

        var secret = _totp.GenerateSecret();
        user.SetPendingTotpSecret(await _cipher.EncryptAsync(secret, PasswordAuthSupport.TotpContext(user.Id), cancellationToken), _clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new TotpEnrollmentResponse(secret, _totp.BuildProvisioningUri(secret, user.Email)));
    }

    public async Task<Result> HandleAsync(ConfirmTotpCommand command, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);
        if (user?.TotpSecretCiphertext is null)
        {
            return Result.Failure(PasswordAuthErrors.TotpNotEnrolled);
        }

        var secret = await _cipher.DecryptAsync(user.TotpSecretCiphertext, PasswordAuthSupport.TotpContext(user.Id), cancellationToken);
        if (!_totp.VerifyCode(secret, command.Code ?? string.Empty, _clock.UtcNow))
        {
            return Result.Failure(PasswordAuthErrors.InvalidTotpCode);
        }

        user.SetTwoFactorEnabled(true, _clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Two-factor authentication enabled for user {UserId}", user.Id);
        return Result.Success();
    }

    /// <summary>New hash + new security stamp, and every refresh token revoked so other sessions must sign in again.</summary>
    private async Task ApplyNewPasswordAsync(User user, string newPassword, string reason, CancellationToken cancellationToken)
    {
        var nowUtc = _clock.UtcNow;
        user.SetPassword(_hasher.Hash(newPassword), nowUtc);
        user.RegisterSuccessfulLogin(nowUtc);

        var activeTokens = await _dbContext.RefreshTokens.Where(t => t.UserId == user.Id && !t.IsRevoked).ToListAsync(cancellationToken);
        activeTokens.ForEach(t => t.Revoke(reason, nowUtc));

        var openSessions = await _dbContext.UserSessions.Where(s => s.UserId == user.Id && !s.IsRevoked).ToListAsync(cancellationToken);
        openSessions.ForEach(s => s.Revoke(nowUtc, reason));

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Password updated for user {UserId} ({Reason}); {Count} sessions revoked", user.Id, reason, activeTokens.Count);
    }

    private string IssueToken(Guid userId, UserActionTokenPurpose purpose, DateTime nowUtc)
    {
        var (raw, hash) = PasswordAuthSupport.NewActionToken();
        _dbContext.UserActionTokens.Add(UserActionToken.Create(userId, purpose, hash, nowUtc));
        return raw;
    }

    private async Task<(UserActionToken? Token, User? User)> ConsumeTokenAsync(string rawToken, UserActionTokenPurpose purpose, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 100)
        {
            return (null, null);
        }

        var hash = PasswordAuthSupport.HashToken(rawToken.Trim());
        var token = await _dbContext.UserActionTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose, cancellationToken);
        if (token is null || !token.IsUsable(_clock.UtcNow))
        {
            return (null, null);
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == token.UserId && !u.IsDeleted, cancellationToken);
        token.MarkUsed(_clock.UtcNow);
        return (token, user);
    }

    private Task<bool> RecentlyIssuedAsync(Guid userId, UserActionTokenPurpose purpose, CancellationToken cancellationToken)
    {
        var since = _clock.UtcNow - _resendCooldown;
        return _dbContext.UserActionTokens.AnyAsync(t => t.UserId == userId && t.Purpose == purpose && t.CreatedAtUtc > since, cancellationToken);
    }

    private async Task InvalidateOpenTokensAsync(Guid userId, UserActionTokenPurpose purpose, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var open = await _dbContext.UserActionTokens
            .Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAtUtc == null)
            .ToListAsync(cancellationToken);
        open.ForEach(t => t.MarkUsed(nowUtc));
    }

    private async Task SendSafelyAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        try
        {
            await _emailSender.SendAsync(to, subject, body, cancellationToken);
        }
        catch (Exception ex) when (ex is System.Net.Mail.SmtpException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            // The token is already stored; the user can request a new email. Never log the link (it contains the token).
            _logger.LogError(ex, "Failed to send account email ({Subject})", subject);
        }
    }
}
