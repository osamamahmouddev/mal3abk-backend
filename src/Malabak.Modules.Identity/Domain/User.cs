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
