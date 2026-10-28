using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Security;

namespace Malabak.Modules.Identity.Application.PasswordAuth;

public sealed record PasswordLoginResponse(
    Guid UserId,
    string FullName,
    IReadOnlyCollection<string> Roles,
    string AccessToken,
    int ExpiresIn,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);

public sealed record TotpEnrollmentResponse(string Secret, string ProvisioningUri);

public static class PasswordAuthErrors
{
    // One message for unknown email, wrong password and wrong role, so accounts can't be enumerated.
    public static readonly Error InvalidCredentials = new("Identity.InvalidCredentials", "Email or password is incorrect.");
    public static readonly Error Throttled = new("Identity.LoginThrottled", "Too many failed attempts. Please wait and try again.");
    public static readonly Error CaptchaRequired = new("Identity.CaptchaRequired", "Please complete the security check.");
    public static readonly Error EmailNotVerified = new("Identity.EmailNotVerified", "Verify your email before signing in.");
    public static readonly Error TwoFactorRequired = new("Identity.TwoFactorRequired", "Enter the code from your authenticator app.");
    public static readonly Error InvalidToken = new("Identity.InvalidOrExpiredToken", "This link is invalid or has expired.");
    public static readonly Error RegistrationFailed = new("Identity.RegistrationFailed", "We couldn't create an account with these details.");
    public static readonly Error InvalidEmail = new("Identity.InvalidEmail", "Enter a valid email address.");
    public static readonly Error InvalidTotpCode = new("Identity.InvalidTotpCode", "The authenticator code is incorrect.");
    public static readonly Error TotpNotEnrolled = new("Identity.TotpNotEnrolled", "Start two-factor enrollment first.");

    public static Error WeakPassword(IEnumerable<string> reasons) => new("Identity.WeakPassword", string.Join(" ", reasons));
}

internal static class PasswordAuthSupport
{
    public static readonly IReadOnlySet<string> PasswordRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        UserRole.VenueOwner,
        UserRole.VenueStaff
    };

    public static bool TryNormalizeEmail(string? email, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256)
        {
            return false;
        }

        try
        {
            normalized = new MailAddress(email.Trim()).Address.ToLowerInvariant();
            return normalized.Contains('@', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>256-bit random token for the link, and its SHA-256 hex hash for storage.</summary>
    public static (string Raw, string Hash) NewActionToken()
    {
        var raw = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (raw, HashToken(raw));
    }

    public static string HashToken(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    public static FieldEncryptionContext TotpContext(Guid userId) => new("users", "totp_secret", userId);

    public static async Task<PasswordLoginResponse> IssueTokensAsync(
        IdentityDbContext db, ITokenService tokenService, User user, Guid sessionId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var tokens = tokenService.GenerateTokens(user, sessionId);
        db.RefreshTokens.Add(RefreshToken.Create(
            Guid.NewGuid(), user.Id, tokenService.HashRefreshToken(tokens.RefreshToken), sessionId, tokens.RefreshTokenExpiresAtUtc, nowUtc));
        await db.SaveChangesAsync(cancellationToken);

        return new PasswordLoginResponse(
            user.Id, user.FullName, user.Roles, tokens.AccessToken, tokens.ExpiresIn, tokens.RefreshToken, tokens.RefreshTokenExpiresAtUtc);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
