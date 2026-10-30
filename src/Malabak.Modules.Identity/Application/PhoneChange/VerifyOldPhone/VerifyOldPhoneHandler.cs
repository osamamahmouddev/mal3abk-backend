using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.PhoneChange.VerifyOldPhone;

public sealed class VerifyOldPhoneHandler : ICommandHandler<VerifyOldPhoneCommand, Result>
{
    private readonly IdentityDbContext _dbContext;
    private readonly IOtpSecurityService _otpSecurityService;
    private readonly IOtpSender _otpSender;
    private readonly IClock _clock;
    private readonly ILogger<VerifyOldPhoneHandler> _logger;

    public VerifyOldPhoneHandler(
        IdentityDbContext dbContext,
        IOtpSecurityService otpSecurityService,
        IOtpSender otpSender,
        IClock clock,
        ILogger<VerifyOldPhoneHandler> logger)
    {
        _dbContext = dbContext;
        _otpSecurityService = otpSecurityService;
        _otpSender = otpSender;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(
        VerifyOldPhoneCommand command,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var request = await _dbContext.PhoneChangeRequests
            .FirstOrDefaultAsync(r => r.Id == command.RequestId && r.UserId == command.UserId, cancellationToken);

        if (request is null)
        {
            return Result.Failure(new Error(
                "Identity.PhoneChangeRequestNotFound",
                "Phone change request not found."));
        }

        if (request.Status != PhoneChangeStatus.Started)
        {
            return Result.Failure(new Error(
                "Identity.InvalidPhoneChangeState",
                "Phone change request is not in Started state."));
        }

        if (nowUtc >= request.ExpiresAtUtc)
        {
            request.Expire(nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result.Failure(new Error(
                "Identity.PhoneChangeRequestExpired",
                "Phone change request has expired. Please start a new request."));
        }

        if (string.IsNullOrWhiteSpace(request.OldPhoneOtpHash))
        {
            return Result.Failure(new Error(
                "Identity.MissingVerificationHash",
                "Verification hash not found on request."));
        }

        var isOldCodeValid = _otpSecurityService.VerifyCode(command.Code, request.OldPhoneOtpHash);
        if (!isOldCodeValid)
        {
            return Result.Failure(new Error(
                "Identity.InvalidOtpCode",
                "Invalid verification code."));
        }

        // Generate OTP for new phone
        var newOtpCode = _otpSecurityService.GenerateSecureOtpCode();
        var newOtpHash = _otpSecurityService.ComputeHmacHash(newOtpCode);

        request.MarkOldPhoneVerified(newOtpHash, nowUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Deliver the verification code to the new phone (never logged)
        if (!await _otpSender.SendAsync(request.NewPhoneNumber, newOtpCode, OtpPurpose.ChangePhoneNew, request.ExpiresAtUtc - nowUtc, cancellationToken))
        {
            return Result.Failure(OtpDeliveryErrors.DeliveryFailed);
        }

        _logger.LogInformation(
            "Phone change request {RequestId} old phone verified for user {UserId}. OTP sent to new phone.",
            request.Id,
            request.UserId);

        return Result.Success();
    }
}
