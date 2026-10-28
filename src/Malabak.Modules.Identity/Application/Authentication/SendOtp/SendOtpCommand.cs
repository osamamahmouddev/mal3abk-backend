using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Application.Authentication.SendOtp;

public sealed record SendOtpCommand(
    string PhoneNumber,
    string TurnstileToken,
    string? RemoteIp = null) : ICommand<Result<SendOtpResponse>>;
