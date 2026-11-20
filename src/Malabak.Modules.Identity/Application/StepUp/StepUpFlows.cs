using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.PasswordAuth;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.StepUp;

public enum StepUpMethod
{
    Password = 0,
    Totp = 1,
    Otp = 2
}

public sealed record StepUpTokenResponse(string StepUpToken, string Scope, int ExpiresInSeconds);

public sealed record SendStepUpOtpCommand(Guid UserId) : ICommand<Result>;

public sealed record VerifyStepUpCommand(Guid UserId, string Scope, StepUpMethod Method, string Secret) : ICommand<Result<StepUpTokenResponse>>;

/// <summary>Used by the step-up endpoint filter to check a presented token against the user's current state.</summary>
public interface IStepUpVerifier
{
    Task<bool> IsValidAsync(Guid userId, string scope, string? stepUpToken, CancellationToken cancellationToken = default);
}

public static class StepUpErrors
{
    public static readonly Error UnknownScope = new("Identity.UnknownStepUpScope", "Unknown step-up scope.");
    public static readonly Error MethodNotAvailable = new("Identity.StepUpMethodNotAvailable", "This verification method isn't available for your account.");
    public static readonly Error VerificationFailed = new("Identity.StepUpFailed", "Verification failed.");
    public static readonly Error OtpRateLimited = new("Identity.OtpRateLimitExceeded", "Too many codes requested. Please wait before trying again.");
}

/// <summary>
/// Re-authentication before sensitive actions (SECURITY.md §3.8, §5.5 step 0): the user proves it's really them
/// with a password, TOTP, or an OTP to their current phone, and receives a 5-minute token for one scope,
/// bound to their security stamp. Admin-destructive scope requires TOTP.
/// </summary>
public sealed class StepUpHandlers :
    ICommandHandler<SendStepUpOtpCommand, Result>,
    ICommandHandler<VerifyStepUpCommand, Result<StepUpTokenResponse>>,
    IStepUpVerifier
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _otpLifetime = TimeSpan.FromMinutes(5);

    private readonly IdentityDbContext _dbContext;
    private readonly IStepUpService _stepUp;
    private readonly IPasswordHasher _hasher;
    private readonly ITotpService _totp;
    private readonly IFieldCipher _cipher;
    private readonly IOtpStore _otpStore;
    private readonly IOtpSecurityService _otpSecurity;
    private readonly IOtpSender _otpSender;
    private readonly IClock _clock;
    private readonly ILogger<StepUpHandlers> _logger;

    public StepUpHandlers(
        IdentityDbContext dbContext,
        IStepUpService stepUp,
        IPasswordHasher hasher,
        ITotpService totp,
        IFieldCipher cipher,
        IOtpStore otpStore,
        IOtpSecurityService otpSecurity,
        IOtpSender otpSender,
        IClock clock,
        ILogger<StepUpHandlers> logger)
    {
        _dbContext = dbContext;
        _stepUp = stepUp;
        _hasher = hasher;
        _totp = totp;
        _cipher = cipher;
        _otpStore = otpStore;
        _otpSecurity = otpSecurity;
        _otpSender = otpSender;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(SendStepUpOtpCommand command, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);
        if (user is null)
        {
            return Result.Failure(StepUpErrors.MethodNotAvailable);
        }

        var key = OtpKey(user.Id);
        if (!await _otpStore.CheckAndRecordSendAttemptAsync(key, maxAttempts: 3, window: TimeSpan.FromHours(1), cancellationToken))
        {
            return Result.Failure(StepUpErrors.OtpRateLimited);
        }

        var code = _otpSecurity.GenerateSecureOtpCode();
        await _otpStore.SaveOtpAsync(key, _otpSecurity.ComputeHmacHash(code), _otpLifetime, maxVerificationAttempts: 5, cancellationToken);

        // Neither the code nor the phone number is logged (SECURITY.md §15.5).
        return await _otpSender.SendAsync(user.PhoneNumber, code, OtpPurpose.StepUp, _otpLifetime, cancellationToken)
            ? Result.Success()
            : Result.Failure(OtpDeliveryErrors.DeliveryFailed);
    }

    public async Task<Result<StepUpTokenResponse>> HandleAsync(VerifyStepUpCommand command, CancellationToken cancellationToken = default)
    {
        var scope = command.Scope?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!StepUpScopes.All.Contains(scope))
        {
            return Result.Failure<StepUpTokenResponse>(StepUpErrors.UnknownScope);
        }

        var nowUtc = _clock.UtcNow;
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);
        if (user is null)
        {
            return Result.Failure<StepUpTokenResponse>(StepUpErrors.VerificationFailed);
        }

        if (user.IsLoginThrottled(nowUtc))
        {
            return Result.Failure<StepUpTokenResponse>(PasswordAuthErrors.Throttled);
        }

        // Destructive admin actions need the strongest factor the admin has: the authenticator once it's on, the
        // password while it's off (two-step verification is optional for admins by the owner's decision).
        if (scope == StepUpScopes.AdminDestructive
            && command.Method != (user.TwoFactorEnabled ? StepUpMethod.Totp : StepUpMethod.Password))
        {
            return Result.Failure<StepUpTokenResponse>(StepUpErrors.MethodNotAvailable);
        }

        var verified = command.Method switch
        {
            StepUpMethod.Password when user.PasswordHash is not null =>
                (bool?)(_hasher.Verify(user.PasswordHash, command.Secret ?? string.Empty) != PasswordVerification.Failed),
            StepUpMethod.Totp when user.TwoFactorEnabled && user.TotpSecretCiphertext is not null =>
                await PasswordAuthSupport.TryClaimTotpAsync(
                    _dbContext,
                    _totp,
                    user,
                    await _cipher.DecryptAsync(user.TotpSecretCiphertext, PasswordAuthSupport.TotpContext(user.Id), cancellationToken),
                    command.Secret?.Trim() ?? string.Empty,
                    nowUtc,
                    cancellationToken),
            StepUpMethod.Otp =>
                (await _otpStore.VerifyAndConsumeOtpAsync(OtpKey(user.Id), command.Secret ?? string.Empty, _otpSecurity, cancellationToken)).IsSuccess,
            _ => null
        };

        if (verified is null)
        {
            return Result.Failure<StepUpTokenResponse>(StepUpErrors.MethodNotAvailable);
        }

        if (verified == false)
        {
            user.RegisterFailedLogin(nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("AUDIT: step-up failed for user {UserId} (scope {Scope}, method {Method})", user.Id, scope, command.Method);
            return Result.Failure<StepUpTokenResponse>(StepUpErrors.VerificationFailed);
        }

        user.RegisterSuccessfulLogin(nowUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var token = _stepUp.IssueStepUpToken(user.Id, scope, TokenLifetime, user.SecurityStamp);
        _logger.LogInformation("AUDIT: step-up granted to user {UserId} (scope {Scope}, method {Method})", user.Id, scope, command.Method);
        return Result.Success(new StepUpTokenResponse(token, scope, (int)TokenLifetime.TotalSeconds));
    }

    public async Task<bool> IsValidAsync(Guid userId, string scope, string? stepUpToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stepUpToken))
        {
            return false;
        }

        var stamp = await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId && !u.IsDeleted)
            .Select(u => u.SecurityStamp)
            .FirstOrDefaultAsync(cancellationToken);

        var valid = stamp is not null && _stepUp.ValidateStepUpToken(stepUpToken, userId, scope, TokenLifetime, stamp);
        _logger.LogInformation("AUDIT: step-up token {Outcome} for user {UserId} (scope {Scope})", valid ? "used" : "rejected", userId, scope);
        return valid;
    }

    private static string OtpKey(Guid userId) => $"stepup:{userId:N}";
}
