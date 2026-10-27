namespace Malabak.Modules.Identity.Application.Authentication.VerifyOtp;

public sealed record VerifyOtpResponse(
    Guid UserId,
    string PhoneNumber,
    string FullName,
    IReadOnlyCollection<string> Roles,
    bool IsNewUser);
