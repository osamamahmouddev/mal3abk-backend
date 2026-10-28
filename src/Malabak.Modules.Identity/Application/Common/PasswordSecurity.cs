namespace Malabak.Modules.Identity.Application.Common;

public enum PasswordVerification
{
    Failed = 0,
    Success = 1,

    /// <summary>Correct, but hashed with weaker parameters than today's; re-hash on this login.</summary>
    SuccessRehashNeeded = 2
}

public interface IPasswordHasher
{
    string Hash(string password);

    PasswordVerification Verify(string hash, string password);
}

public interface IPasswordPolicy
{
    /// <summary>Returns the list of violations (empty when the password is acceptable).</summary>
    Task<IReadOnlyList<string>> ValidateAsync(string password, string? email, CancellationToken cancellationToken = default);
}

public interface IBreachedPasswordChecker
{
    /// <summary>True when the password appears in known breaches (HIBP k-anonymity; only a 5-char hash prefix leaves the server).</summary>
    Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default);
}

public interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default);
}

public interface ITotpService
{
    string GenerateSecret();

    bool VerifyCode(string base32Secret, string code, DateTime nowUtc);

    string BuildProvisioningUri(string base32Secret, string accountName);
}
