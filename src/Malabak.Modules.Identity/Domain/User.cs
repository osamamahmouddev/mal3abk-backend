using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Domain;

public sealed class User : Entity<Guid>
{
    private readonly List<string> _roles = [];

    private User()
    {
    }

    public string PhoneNumber { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string? PasswordHash { get; private set; }
    public string SecurityStamp { get; private set; } = string.Empty;
    public bool IsPhoneNumberConfirmed { get; private set; }
    public bool IsEmailConfirmed { get; private set; }
    public bool TwoFactorEnabled { get; private set; }
    public DateTime? LastPhoneChangeAtUtc { get; private set; }
    // Profile: city/area only (never precise location), optional football info, language. Minimum data by design.
    public string? City { get; private set; }
    public string? Area { get; private set; }
    public SkillLevel? SkillLevel { get; private set; }
    public string Language { get; private set; } = UserLanguages.Arabic;
    public DateTime? OnboardingCompletedAtUtc { get; private set; }

    private readonly List<PlayerPosition> _positions = [];
    public IReadOnlyCollection<PlayerPosition> Positions => _positions.AsReadOnly();

    public const int MaxNameLength = 100;
    public const int MaxPlaceLength = 100;

    public int FailedLoginCount { get; private set; }
    public DateTime? LoginThrottledUntilUtc { get; private set; }

    /// <summary>TOTP shared secret, encrypted with the field cipher (AAD bound to this user).</summary>
    public byte[]? TotpSecretCiphertext { get; private set; }

    /// <summary>Failed password attempts before the login also requires a CAPTCHA (Turnstile).</summary>
    public const int CaptchaAfterFailures = 3;

    /// <summary>Failed attempts after which an increasing (never permanent) delay applies.</summary>
    public const int ThrottleAfterFailures = 5;

    public static readonly TimeSpan MaxThrottle = TimeSpan.FromMinutes(15);

    public IReadOnlyCollection<string> Roles => _roles.AsReadOnly();

    public static User CreatePlayer(
        Guid id,
        string phoneNumber,
        string fullName,
        DateTime createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        var user = new User
        {
            Id = id,
            PhoneNumber = phoneNumber.Trim(),
            FullName = fullName.Trim(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsPhoneNumberConfirmed = true,
            CreatedAtUtc = createdAtUtc
        };

        user._roles.Add(UserRole.Player);
        return user;
    }

    public static User CreateStaff(
        Guid id,
        string email,
        string phoneNumber,
        string passwordHash,
        string fullName,
        string role,
        DateTime createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        var user = new User
        {
            Id = id,
            Email = email.Trim().ToLowerInvariant(),
            PhoneNumber = phoneNumber.Trim(),
            PasswordHash = passwordHash,
            FullName = fullName.Trim(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsEmailConfirmed = true,
            CreatedAtUtc = createdAtUtc
        };

        user._roles.Add(role);
        return user;
    }

    /// <summary>Self-registered venue owner: email starts unconfirmed until the verification link is used.</summary>
    public static User CreateVenueOwner(Guid id, string email, string phoneNumber, string passwordHash, string fullName, DateTime createdAtUtc)
    {
        var user = CreateStaff(id, email, phoneNumber, passwordHash, fullName, UserRole.VenueOwner, createdAtUtc);
        user.IsEmailConfirmed = false;
        return user;
    }

    /// <summary>Profile setup from onboarding (and later edits). Completing it marks onboarding done the first time.</summary>
    public void UpdateProfile(
        string fullName, string city, string area, IEnumerable<PlayerPosition>? positions, SkillLevel? skillLevel, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(area);

        if (fullName.Trim().Length > MaxNameLength || city.Trim().Length > MaxPlaceLength || area.Trim().Length > MaxPlaceLength)
        {
            throw new ArgumentException("Profile value too long.");
        }

        FullName = fullName.Trim();
        City = city.Trim();
        Area = area.Trim();
        SkillLevel = skillLevel;
        _positions.Clear();
        _positions.AddRange((positions ?? []).Distinct());
        OnboardingCompletedAtUtc ??= nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void SetLanguage(string language, DateTime nowUtc)
    {
        if (!UserLanguages.IsSupported(language))
        {
            throw new ArgumentException("Unsupported language.", nameof(language));
        }

        Language = language;
        UpdatedAtUtc = nowUtc;
    }

    public bool IsOnboardingComplete => OnboardingCompletedAtUtc is not null;

    public void ConfirmEmail(DateTime nowUtc)
    {
        IsEmailConfirmed = true;
        UpdatedAtUtc = nowUtc;
    }

    public bool IsLoginThrottled(DateTime nowUtc) => LoginThrottledUntilUtc is { } until && until > nowUtc;

    /// <summary>
    /// Progressive delay instead of a permanent lockout (a permanent lock would let anyone lock a victim out):
    /// 1, 2, 4, 8 … minutes after the 5th failure, capped at 15 minutes.
    /// </summary>
    public void RegisterFailedLogin(DateTime nowUtc)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= ThrottleAfterFailures)
        {
            var exponent = Math.Min(FailedLoginCount - ThrottleAfterFailures, 4);
            var delay = TimeSpan.FromMinutes(Math.Pow(2, exponent));
            LoginThrottledUntilUtc = nowUtc + (delay < MaxThrottle ? delay : MaxThrottle);
        }

        UpdatedAtUtc = nowUtc;
    }

    public void RegisterSuccessfulLogin(DateTime nowUtc)
    {
        FailedLoginCount = 0;
        LoginThrottledUntilUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Stores a pending TOTP secret; 2FA is only enabled once a code from it is confirmed.</summary>
    public void SetPendingTotpSecret(byte[] ciphertext, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        TotpSecretCiphertext = ciphertext;
        TwoFactorEnabled = false;
        UpdatedAtUtc = nowUtc;
    }

    public void AddRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        if (!_roles.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            _roles.Add(role);
        }
    }

    public void RemoveRole(string role)
    {
        _roles.RemoveAll(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
    }

    public void UpdateSecurityStamp(DateTime nowUtc)
    {
        SecurityStamp = Guid.NewGuid().ToString("N");
        UpdatedAtUtc = nowUtc;
    }

    public void ChangePhoneNumber(string newPhoneNumber, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPhoneNumber);
        PhoneNumber = newPhoneNumber.Trim();
        IsPhoneNumberConfirmed = true;
        LastPhoneChangeAtUtc = nowUtc;
        UpdateSecurityStamp(nowUtc);
    }

    public void SetTwoFactorEnabled(bool enabled, DateTime nowUtc)
    {
        TwoFactorEnabled = enabled;
        UpdatedAtUtc = nowUtc;
    }

    public void SetPassword(string passwordHash, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        UpdateSecurityStamp(nowUtc);
    }
}
