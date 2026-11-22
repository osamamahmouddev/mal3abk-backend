using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.PasswordAuth;
using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.EmailChange;

public sealed record StartEmailChangeCommand(Guid UserId, string? NewEmail, string? StepUpToken) : ICommand<Result>;

public sealed record ConfirmEmailChangeCommand(Guid UserId, string? Code) : ICommand<Result>;

public static class EmailChangeErrors
{
    public static readonly Error InvalidStepUpToken = new(
        "Identity.InvalidStepUpToken", "A valid step-up verification token issued within the last 5 minutes is required.");
    public static readonly Error NoEmail = Error.Validation("Identity.NoEmailOnAccount", "This account signs in without an email.");
    public static readonly Error InvalidEmail = Error.Validation("Identity.InvalidEmail", "Enter a valid email address.");
    public static readonly Error SameEmail = Error.Validation("Identity.SameEmail", "This is already your email.");

    /// <summary>Deliberately vague: whether another account uses the address is not revealed (SECURITY.md §5.5).</summary>
    public static readonly Error UnableToUseEmail = Error.Validation("Identity.UnableToUseEmail", "This email can't be used. Try another one.");
    public static readonly Error NoPendingChange = Error.Validation("Identity.EmailChangeExpired", "The code expired or was not requested. Start again.");
    public static readonly Error InvalidCode = Error.Validation("Identity.InvalidOtpCode", "Invalid verification code.");
    public static readonly Error TooManyAttempts = Error.Validation("Identity.MaxAttemptsExceeded", "Too many wrong codes. Start again.");
}

/// <summary>
/// Changing the email an owner or staff member signs in with (the phone change rules of SECURITY.md §5, applied to email):
/// a fresh step-up (password) to start, a code sent to the new address to prove it is theirs, then every session is
/// signed out and the old address is told. The email only changes once the code is entered.
/// </summary>
public sealed class EmailChangeHandlers : ICommandHandler<StartEmailChangeCommand, Result>, ICommandHandler<ConfirmEmailChangeCommand, Result>
{
    private static readonly TimeSpan _stepUpMaxAge = TimeSpan.FromMinutes(5);

    private readonly IdentityDbContext _dbContext;
    private readonly IStepUpService _stepUp;
    private readonly IOtpSecurityService _otp;
    private readonly IEmailSender _email;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;
    private readonly ILogger<EmailChangeHandlers> _logger;
    private readonly ISessionStore? _sessionStore;

    public EmailChangeHandlers(
        IdentityDbContext dbContext, IStepUpService stepUp, IOtpSecurityService otp, IEmailSender email, IAuditLog audit, IClock clock,
        ILogger<EmailChangeHandlers> logger, ISessionStore? sessionStore = null)
    {
        _dbContext = dbContext;
        _stepUp = stepUp;
        _otp = otp;
        _email = email;
        _audit = audit;
        _clock = clock;
        _logger = logger;
        _sessionStore = sessionStore;
    }

    public async Task<Result> HandleAsync(StartEmailChangeCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.StepUpToken)
            || !_stepUp.ValidateStepUpToken(command.StepUpToken, command.UserId, StepUpScopes.ChangeEmail, _stepUpMaxAge))
        {
            return Result.Failure(EmailChangeErrors.InvalidStepUpToken);
        }

        if (!PasswordAuthSupport.TryNormalizeEmail(command.NewEmail, out var newEmail))
        {
            return Result.Failure(EmailChangeErrors.InvalidEmail);
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);
        if (user?.Email is null)
        {
            return Result.Failure(EmailChangeErrors.NoEmail);
        }

        if (string.Equals(user.Email, newEmail, StringComparison.Ordinal))
        {
            return Result.Failure(EmailChangeErrors.SameEmail);
        }

        // Accounts in their deletion grace period still own their address.
        if (await _dbContext.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == newEmail && u.Id != user.Id, cancellationToken))
        {
            return Result.Failure(EmailChangeErrors.UnableToUseEmail);
        }

        var nowUtc = _clock.UtcNow;
        await SpendPendingAsync(user.Id, nowUtc, cancellationToken);

        var id = Guid.CreateVersion7();
        var code = _otp.GenerateSecureOtpCode();
        _dbContext.UserActionTokens.Add(UserActionToken.CreateEmailChange(id, user.Id, newEmail, _otp.ComputeHmacHash(Bind(id, code)), nowUtc));
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _email.SendAsync(newEmail, EmailTemplates.EmailChangeCode(code), cancellationToken);
        await _audit.RecordAsync(new AuditEntry("user.email_change_requested", "User", user.Id.ToString("D"), user.Id, AuditActorType.User), cancellationToken);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(ConfirmEmailChangeCommand command, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var pending = await _dbContext.UserActionTokens
            .Where(t => t.UserId == command.UserId && t.Purpose == UserActionTokenPurpose.EmailChange && t.UsedAtUtc == null && t.ExpiresAtUtc > nowUtc)
            .OrderByDescending(t => t.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (pending?.NewEmail is null)
        {
            return Result.Failure(EmailChangeErrors.NoPendingChange);
        }

        if (string.IsNullOrWhiteSpace(command.Code) || !_otp.VerifyCode(Bind(pending.Id, command.Code.Trim()), pending.TokenHash))
        {
            var spent = pending.RegisterFailedAttempt(nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Result.Failure(spent ? EmailChangeErrors.TooManyAttempts : EmailChangeErrors.InvalidCode);
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);
        if (user?.Email is null)
        {
            return Result.Failure(EmailChangeErrors.NoEmail);
        }

        var oldEmail = user.Email;
        var newEmail = pending.NewEmail;
        List<UserSession> sessions;
        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            if (await _dbContext.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == newEmail && u.Id != user.Id, cancellationToken))
            {
                return Result.Failure(EmailChangeErrors.UnableToUseEmail);
            }

            pending.MarkUsed(nowUtc);
            user.SetEmail(newEmail);
            user.ConfirmEmail(nowUtc);
            user.UpdateSecurityStamp(nowUtc);

            // Every device signs in again with the new address, this one included.
            foreach (var token in await _dbContext.RefreshTokens.Where(t => t.UserId == user.Id && !t.IsRevoked).ToListAsync(cancellationToken))
            {
                token.Revoke("email_changed", nowUtc);
            }

            sessions = await _dbContext.UserSessions.Where(s => s.UserId == user.Id && !s.IsRevoked).ToListAsync(cancellationToken);
            sessions.ForEach(s => s.Revoke(nowUtc, "email_changed"));

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // The unique index on email is the last guard against a race with another account.
                return Result.Failure(EmailChangeErrors.UnableToUseEmail);
            }
        }

        if (_sessionStore is not null)
        {
            await _sessionStore.MarkRevokedAsync(sessions.Select(s => s.Id).ToList(), cancellationToken);
        }

        await _audit.RecordAsync(new AuditEntry("user.email_changed", "User", user.Id.ToString("D"), user.Id, AuditActorType.User), cancellationToken);
        try
        {
            await _email.SendAsync(oldEmail, EmailTemplates.EmailChanged(Mask(newEmail)), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The change is done; a failed notice must not undo it.
            _logger.LogWarning(ex, "Could not send the email-changed notice for user {UserId}", user.Id);
        }

        return Result.Success();
    }

    /// <summary>The code is hashed together with its token id, so the same code for two people never gives the same hash.</summary>
    private static string Bind(Guid tokenId, string code) => $"{tokenId:N}:{code}";

    private static string Mask(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? $"•••{email[Math.Max(at, 0)..]}" : $"{email[0]}•••{email[at..]}";
    }

    private async Task SpendPendingAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var open = await _dbContext.UserActionTokens
            .Where(t => t.UserId == userId && t.Purpose == UserActionTokenPurpose.EmailChange && t.UsedAtUtc == null)
            .ToListAsync(cancellationToken);
        open.ForEach(t => t.MarkUsed(nowUtc));
    }
}
