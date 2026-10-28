using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.PhoneChange.StartPhoneChange;

public sealed class StartPhoneChangeHandler : ICommandHandler<StartPhoneChangeCommand, Result<StartPhoneChangeResponse>>
{
    private static readonly TimeSpan _stepUpMaxAge = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _cooldownPeriod = TimeSpan.FromDays(7);
    private static readonly TimeSpan _requestTtl = TimeSpan.FromMinutes(15);

    private readonly IdentityDbContext _dbContext;
    private readonly IStepUpService _stepUpService;
    private readonly IPhoneNumberNormalizer _normalizer;
    private readonly IOtpSecurityService _otpSecurityService;
    private readonly ISmsAlertService _smsAlertService;
    private readonly IClock _clock;
    private readonly ILogger<StartPhoneChangeHandler> _logger;

    public StartPhoneChangeHandler(
        IdentityDbContext dbContext,
        IStepUpService stepUpService,
        IPhoneNumberNormalizer normalizer,
        IOtpSecurityService otpSecurityService,
        ISmsAlertService smsAlertService,
        IClock clock,
        ILogger<StartPhoneChangeHandler> logger)
    {
        _dbContext = dbContext;
        _stepUpService = stepUpService;
        _normalizer = normalizer;
        _otpSecurityService = otpSecurityService;
        _smsAlertService = smsAlertService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<StartPhoneChangeResponse>> HandleAsync(
        StartPhoneChangeCommand command,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        // 1. Verify Step-Up token (required <= 5 mins, scope: change_phone)
        var isStepUpValid = _stepUpService.ValidateStepUpToken(
            command.StepUpToken,
            command.UserId,
            "change_phone",
            _stepUpMaxAge);

        if (!isStepUpValid)
        {
            return Result.Failure<StartPhoneChangeResponse>(new Error(
                "Identity.InvalidStepUpToken",
                "A valid step-up verification token issued within the last 5 minutes is required."));
        }

        // 2. Validate & normalize new phone number
        var normalizeResult = _normalizer.Normalize(command.NewPhoneNumber);
        if (normalizeResult.IsFailure)
        {
            return Result.Failure<StartPhoneChangeResponse>(normalizeResult.Error);
        }

        var normalizedNewPhone = normalizeResult.Value;

        // 3. User verification
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<StartPhoneChangeResponse>(new Error(
                "Identity.UserNotFound",
                "User not found."));
        }

        if (string.Equals(user.PhoneNumber, normalizedNewPhone, StringComparison.Ordinal))
        {
            return Result.Failure<StartPhoneChangeResponse>(new Error(
                "Identity.SamePhoneNumber",
                "The new phone number cannot be the same as your current phone number."));
        }

        // 4. Enforce 7-day cooldown
        if (user.LastPhoneChangeAtUtc.HasValue && nowUtc - user.LastPhoneChangeAtUtc.Value < _cooldownPeriod)
        {
            var nextAllowedDate = user.LastPhoneChangeAtUtc.Value.Add(_cooldownPeriod);
            return Result.Failure<StartPhoneChangeResponse>(new Error(
                "Identity.PhoneChangeCooldownActive",
                $"Phone number can only be changed once every 7 days. Next change allowed on: {nextAllowedDate:yyyy-MM-dd HH:mm:ss} UTC."));
        }

        // 5. Ensure new phone is not already in use by another user
        var isTaken = await _dbContext.Users
            .AnyAsync(u => u.PhoneNumber == normalizedNewPhone && u.Id != user.Id, cancellationToken);

        if (isTaken)
        {
            return Result.Failure<StartPhoneChangeResponse>(new Error(
                "Identity.PhoneNumberAlreadyInUse",
                "The requested phone number is already registered to another account."));
        }

        // 6. Expire any existing open phone change requests for this user
        var pendingRequests = await _dbContext.PhoneChangeRequests
            .Where(r => r.UserId == user.Id && (r.Status == PhoneChangeStatus.Started || r.Status == PhoneChangeStatus.OldVerified))
            .ToListAsync(cancellationToken);

        foreach (var req in pendingRequests)
        {
            req.Expire(nowUtc);
        }

        // 7. Generate OTP for old phone
        var oldOtpCode = _otpSecurityService.GenerateSecureOtpCode();
        var oldOtpHash = _otpSecurityService.ComputeHmacHash(oldOtpCode);

        var expiresAtUtc = nowUtc.Add(_requestTtl);
        var changeRequest = PhoneChangeRequest.Start(
            id: Guid.NewGuid(),
            userId: user.Id,
            oldPhoneNumber: user.PhoneNumber,
            newPhoneNumber: normalizedNewPhone,
            oldPhoneOtpHash: oldOtpHash,
            expiresAtUtc: expiresAtUtc,
            nowUtc: nowUtc);

        _dbContext.PhoneChangeRequests.Add(changeRequest);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 8. Dispatch verification OTP SMS to old phone
        await _smsAlertService.SendSecurityAlertAsync(
            user.PhoneNumber,
            $"Your verification code to start changing your Malabak phone number is: {oldOtpCode}. Valid for 15 minutes.",
            cancellationToken);

        _logger.LogInformation(
            "Phone change request {RequestId} started for user {UserId}. Verification OTP sent to current phone.",
            changeRequest.Id,
            user.Id);

        return Result.Success(new StartPhoneChangeResponse(changeRequest.Id, changeRequest.ExpiresAtUtc));
    }
}
