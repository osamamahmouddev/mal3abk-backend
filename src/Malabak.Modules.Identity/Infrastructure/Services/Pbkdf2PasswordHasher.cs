using System.Globalization;
using System.Security.Cryptography;
using Malabak.Modules.Identity.Application.Common;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// PBKDF2-HMAC-SHA256 with 600k iterations (OWASP 2023+ guidance; SECURITY.md §4.2 requires at least 210k).
/// Format: <c>pbkdf2-sha256$iterations$salt$hash</c> (base64), so iterations can be raised later and old
/// hashes upgraded transparently on the next successful login.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const int Iterations = 600_000;
    private const string _scheme = "pbkdf2-sha256";
    private const int _saltSize = 16;
    private const int _hashSize = 32;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(_saltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, _hashSize);
        return string.Join('$', _scheme, Iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public PasswordVerification Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(password))
        {
            return PasswordVerification.Failed;
        }

        var parts = hash.Split('$');
        if (parts.Length != 4 || parts[0] != _scheme
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations <= 0)
        {
            return PasswordVerification.Failed;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return PasswordVerification.Failed;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return PasswordVerification.Failed;
        }

        return iterations < Iterations ? PasswordVerification.SuccessRehashNeeded : PasswordVerification.Success;
    }
}
