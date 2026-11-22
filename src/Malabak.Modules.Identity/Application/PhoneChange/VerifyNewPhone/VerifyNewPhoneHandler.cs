using System.Security.Cryptography;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.PhoneChange.VerifyNewPhone;

public sealed class VerifyNewPhoneHandler : ICommandHandler<VerifyNewPhoneCommand, Result<VerifyNewPhoneResponse>>
{
    private readonly IdentityDbContext _dbContext;
    private readonly IOtpSecurityService _otpSecurityService;
    private readonly ITokenService _tokenService;
    private readonly ISmsAlertService _smsAlertService;
    private readonly ISessionStore? _sessionStore;
    private readonly IClock _clock;
    private readonly ILogger<VerifyNewPhoneHandler> _logger;

    public VerifyNewPhoneHandler(
        IdentityDbContext dbContext,
        IOtpSecurityService otpSecurityService,
        ITokenService tokenService,
        ISmsAlertService smsAlertService,
        IClock clock,
        ILogger<VerifyNewPhoneHandler> logger,
        ISessionStore? sessionStore = null)
    {
        _dbContext = dbContext;
        _otpSecurityService = otpSecurityService;
        _tokenService = tokenService;
        _smsAlertService = smsAlertService;
        _sessionStore = sessionStore;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<VerifyNewPhoneResponse>> HandleAsync(
        VerifyNewPhoneCommand command,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var request = await _dbContext.PhoneChangeRequests
            .FirstOrDefaultAsync(r => r.Id == command.RequestId && r.UserId == command.UserId, cancellationToken);

        if (request is null)
        {
            return Result.Failure<VerifyNewPhoneResponse>(new Error(
                "Identity.PhoneChangeRequestNotFound",
                "Phone change request not found."));
        }

        if (request.Status == PhoneChangeStatus.Failed)
        {
            return Result.Failure<VerifyNewPhoneResponse>(PhoneChangeErrors.MaxAttemptsExceeded);
        }

        if (request.Status != PhoneChangeStatus.OldVerified)
        {
            return Result.Failure<VerifyNewPhoneResponse>(new Error(
                "Identity.InvalidPhoneChangeState",
                "Old phone number must be verified before verifying new phone."));
        }

        if (nowUtc >= request.ExpiresAtUtc)
        {
            request.Expire(nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result.Failure<VerifyNewPhoneResponse>(new Error(
                "Identity.PhoneChangeRequestExpired",
                "Phone change request has expired. Please start a new request."));
        }

        if (string.IsNullOrWhiteSpace(request.NewPhoneOtpHash))
        {
            return Result.Failure<VerifyNewPhoneResponse>(new Error(
                "Identity.MissingVerificationHash",
                "Verification hash not found on request."));
        }

        var isNewCodeValid = _otpSecurityService.VerifyCode(command.Code, request.NewPhoneOtpHash);
        if (!isNewCodeValid)
        {
            var locked = request.RegisterFailedNewAttempt(nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (locked)
            {
                _logger.LogWarning("Phone change request {RequestId} locked after too many wrong new-phone codes.", request.Id);
                return Result.Failure<VerifyNewPhoneResponse>(PhoneChangeErrors.MaxAttemptsExceeded);
            }

            return Result.Failure<VerifyNewPhoneResponse>(new Error(
                "Identity.InvalidOtpCode",
                "Invalid verification code."));
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<VerifyNewPhoneResponse>(new Error(
                "Identity.UserNotFound",
                "User not found."));
        }

        // Generate 24-hour recovery token
        var recoveryTokenRaw = GenerateSecureRecoveryToken();
        var recoveryTokenHash = _tokenService.HashRefreshToken(recoveryTokenRaw);

        // Atomic transaction: Update user phone, rotate security stamp, revoke active refresh tokens, complete request
        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            // Re-verify new phone is still not taken by another user. Accounts in their deletion grace period
            // still own their number, so they are included.
            var isTaken = await _dbContext.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => u.PhoneNumber == request.NewPhoneNumber && u.Id != user.Id, cancellationToken);

            if (isTaken)
            {
                return Result.Failure<VerifyNewPhoneResponse>(PhoneChangeErrors.UnableToUsePhoneNumber);
            }

            // 1. Update user phone number, confirmed status, last change time, and security stamp
            user.ChangePhoneNumber(request.NewPhoneNumber, nowUtc);

            // 2. Revoke all active remote sessions and tokens
            var activeTokens = await _dbContext.RefreshTokens
                .Where(t => t.UserId == user.Id && !t.IsRevoked)
                .ToListAsync(cancellationToken);

            foreach (var token in activeTokens)
            {
                token.Revoke("phone_changed", nowUtc);
            }

            var openSessions = await _dbContext.UserSessions
                .Where(s => s.UserId == user.Id && !s.IsRevoked)
                .ToListAsync(cancellationToken);

            foreach (var session in openSessions)
            {
                session.Revoke(nowUtc, "phone_changed");
            }

            // 3. Complete phone change request
            request.Complete(recoveryTokenHash, nowUtc);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                if (_sessionStore != null)
                {
                    await _sessionStore.MarkRevokedAsync(openSessions.Select(s => s.Id).ToList(), cancellationToken);
                }
            }
            catch (DbUpdateException ex)
            {
                // The unique index on users.phone_number is the last guard against two accounts taking the same
                // number at once: the loser gets the same generic answer, not a 500 (SECURITY.md §5.5 step 3).
                _logger.LogWarning(ex, "Phone change request {RequestId} lost a race for its target number.", request.Id);
                return Result.Failure<VerifyNewPhoneResponse>(PhoneChangeErrors.UnableToUsePhoneNumber);
            }
        }

        // 4. Alert the OLD phone with a 24-hour recovery link. The link carries a live token and the
        // numbers are PII, so neither goes to the logs (SECURITY.md §15.5).
        var recoveryLink = $"https://malabak.app/recover-phone?token={recoveryTokenRaw}";

        await _smsAlertService.SendSecurityAlertAsync(
            request.OldPhoneNumber,
            SecurityAlert.PhoneChanged(request.NewPhoneNumber, recoveryLink),
            cancellationToken);

        _logger.LogInformation(
            "Phone change request {RequestId} completed for user {UserId}. Recovery alert sent to the old phone.",
            request.Id,
            user.Id);

        return Result.Success(new VerifyNewPhoneResponse(true, request.NewPhoneNumber));
    }

    private static string GenerateSecureRecoveryToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
