namespace Malabak.Modules.Identity.Application.Common;

public sealed record TokenResult(
    string AccessToken,
    int ExpiresIn,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);
