using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Application.Authentication.VerifyOtp;

public sealed record VerifyOtpCommand(
    string PhoneNumber,
    string Code,
    string? FullName = null) : ICommand<Result<VerifyOtpResponse>>;
