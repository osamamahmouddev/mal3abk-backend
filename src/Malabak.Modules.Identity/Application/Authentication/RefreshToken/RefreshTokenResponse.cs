namespace Malabak.Modules.Identity.Application.Authentication.RefreshToken;

public sealed record RefreshTokenResponse(
    string AccessToken,
    int ExpiresIn,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);
