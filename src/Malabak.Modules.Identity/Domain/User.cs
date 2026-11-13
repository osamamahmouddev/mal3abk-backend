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
    public const int MaxAvatarUrlLength = 300;

    /// <summary>Media-pipeline URL of the re-encoded avatar (never a client-supplied external URL).</summary>
    public string? AvatarUrl { get; private set; }

    public int FailedLoginCount { get; private set; }
    public DateTime? LoginThrottledUntilUtc { get; private set; }

    /// <summary>TOTP shared secret, encrypted with the field cipher (AAD bound to this user).</summary>
    public byte[]? TotpSecretCiphertext { get; private set; }

    /// <summary>Highest TOTP time step already accepted for a sign-in/step-up; a code from that step or older is a replay.</summary>
    public long? TotpLastUsedStep { get; private set; }

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

    /// <summary>How long a deleted account is kept (disputes, refunds, fraud checks) before its personal data is erased.</summary>
    public static readonly TimeSpan DeletionRetention = TimeSpan.FromDays(30);

    public const string AnonymizedName = "لاعب محذوف / Deleted Player";

    public DateTime? DeletionRequestedAtUtc { get; private set; }
    public DateTime? AnonymizedAtUtc { get; private set; }

    /// <summary>Soft delete: the account stops working immediately; personal data is erased after the retention window.</summary>
    public void RequestDeletion(DateTime nowUtc)
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        DeletionRequestedAtUtc = nowUtc;
        UpdateSecurityStamp(nowUtc);
    }

    /// <summary>A deleted account can be restored by its owner until the retention window ends (ADR-0007).</summary>
    public bool CanRestore(DateTime nowUtc) =>
        IsDeleted && AnonymizedAtUtc is null && DeletionRequestedAtUtc is { } requested && nowUtc < requested + DeletionRetention;

    public void Restore(DateTime nowUtc)
    {
        if (!CanRestore(nowUtc))
        {
            throw new InvalidOperationException("The account can no longer be restored.");
        }

        IsDeleted = false;
        DeletionRequestedAtUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Irreversibly replaces personal data. The row stays so bookings and payments keep a valid (now anonymous) owner;
    /// the phone number is released so it can sign up again.
    /// </summary>
    public void Anonymize(DateTime nowUtc)
    {
        if (!IsDeleted || AnonymizedAtUtc is not null)
        {
            return;
        }

        // Unique, fits the 32-char column, and can never match a normalized phone number (those start with '+').
        PhoneNumber = Id.ToString("N");
        Email = null;
        FullName = AnonymizedName;
        PasswordHash = null;
        IsPhoneNumberConfirmed = false;
        IsEmailConfirmed = false;
        TwoFactorEnabled = false;
        TotpSecretCiphertext = null;
        City = null;
        Area = null;
        AvatarUrl = null;
        SkillLevel = null;
        _positions.Clear();
        AnonymizedAtUtc = nowUtc;
        UpdateSecurityStamp(nowUtc);
    }

    public void SetAvatar(string? avatarUrl, DateTime nowUtc)
    {
        if (avatarUrl is { Length: 0 or > MaxAvatarUrlLength })
        {
            throw new ArgumentException("Invalid avatar URL.", nameof(avatarUrl));
        }

        AvatarUrl = avatarUrl;
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
        TotpLastUsedStep = null;
        TwoFactorEnabled = false;
        UpdatedAtUtc = nowUtc;
    }

    public void RecordTotpStep(long step, DateTime nowUtc)
    {
        TotpLastUsedStep = step;
        UpdatedAtUtc = nowUtc;
    }

    public void SetEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        Email = email.Trim().ToLowerInvariant();
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

    public const int MaxSuspensionReasonLength = 500;

    /// <summary>Set while an admin has suspended the account (TASK-701). Suspended accounts cannot sign in or refresh.</summary>
    public DateTime? SuspendedAtUtc { get; private set; }
    public string? SuspensionReason { get; private set; }
    public bool IsSuspended => SuspendedAtUtc is not null;

    /// <summary>Suspends the account and rotates the security stamp; the caller revokes the open sessions.</summary>
    public void Suspend(string reason, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        SuspendedAtUtc = nowUtc;
        SuspensionReason = reason.Trim().Length > MaxSuspensionReasonLength ? reason.Trim()[..MaxSuspensionReasonLength] : reason.Trim();
        UpdateSecurityStamp(nowUtc);
    }

    public void Reactivate(DateTime nowUtc)
    {
        SuspendedAtUtc = null;
        SuspensionReason = null;
        FailedLoginCount = 0;
        LoginThrottledUntilUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    public void SetPassword(string passwordHash, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        UpdateSecurityStamp(nowUtc);
    }
}
