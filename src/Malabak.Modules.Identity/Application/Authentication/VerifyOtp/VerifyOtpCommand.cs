using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Application.Authentication.VerifyOtp;

/// <param name="RestoreAccount">Explicit confirmation to cancel a pending account deletion (ADR-0007 grace period).</param>
public sealed record VerifyOtpCommand(
    string PhoneNumber,
    string Code,
    string? FullName = null,
    bool RestoreAccount = false) : ICommand<Result<VerifyOtpResponse>>;
