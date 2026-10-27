namespace Malabak.Modules.Identity.Application.Authentication.VerifyOtp;

public sealed record VerifyOtpResponse(
    Guid UserId,
    string PhoneNumber,
    string FullName,
    IReadOnlyCollection<string> Roles,
    bool IsNewUser,
    string? AccessToken = null,
    int? ExpiresIn = null,
    string? RefreshToken = null,
    DateTime? RefreshTokenExpiresAtUtc = null);
