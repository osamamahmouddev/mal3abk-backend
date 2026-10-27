namespace Malabak.Modules.Identity.Application.Authentication.SendOtp;

public sealed record SendOtpResponse(
    string NormalizedPhoneNumber,
    int ExpiresInSeconds,
    string Message);
