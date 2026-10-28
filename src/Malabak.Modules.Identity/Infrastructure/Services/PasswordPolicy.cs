using Malabak.Modules.Identity.Application.Common;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Length over complexity (SECURITY.md §4.2): 10–128 characters, not a common password, not the email,
/// and not found in known breaches. No forced symbol/digit rules.
/// </summary>
public sealed class PasswordPolicy : IPasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    private static readonly HashSet<string> _common = new(StringComparer.OrdinalIgnoreCase)
    {
        "1234567890", "12345678910", "0123456789", "password123", "password1234", "qwertyuiop", "1q2w3e4r5t",
        "iloveyou123", "malabak123", "mal3abk123", "aaaaaaaaaa", "abcdefghij", "football123", "egypt12345"
    };

    private readonly IBreachedPasswordChecker _breachedChecker;

    public PasswordPolicy(IBreachedPasswordChecker breachedChecker)
    {
        _breachedChecker = breachedChecker;
    }

    public async Task<IReadOnlyList<string>> ValidateAsync(string password, string? email, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            errors.Add($"Password must be at least {MinLength} characters.");
            return errors;
        }

        if (password.Length > MaxLength)
        {
            errors.Add($"Password must be at most {MaxLength} characters.");
            return errors;
        }

        if (_common.Contains(password) || password.Distinct().Count() <= 2)
        {
            errors.Add("Password is too common.");
        }

        var emailLocalPart = email?.Split('@')[0];
        if (!string.IsNullOrWhiteSpace(emailLocalPart) && emailLocalPart.Length >= 4
            && password.Contains(emailLocalPart, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Password must not contain your email.");
        }

        if (errors.Count == 0 && await _breachedChecker.IsBreachedAsync(password, cancellationToken))
        {
            errors.Add("This password has appeared in a data breach. Choose a different one.");
        }

        return errors;
    }
}
