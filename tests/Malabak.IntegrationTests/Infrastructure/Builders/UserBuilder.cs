using Malabak.Modules.Identity.Domain;

namespace Malabak.IntegrationTests.Infrastructure.Builders;

public sealed class UserBuilder
{
    private Guid _id = Guid.NewGuid();
    private string _phoneNumber = "+201012345678";
    private string _fullName = "Test Player";
    private string? _email;
    private string? _role = UserRole.Player;
    private DateTime _createdAtUtc = DateTime.UtcNow;

    public UserBuilder WithId(Guid id)
    {
        _id = id;
        return this;
    }

    public UserBuilder WithPhoneNumber(string phoneNumber)
    {
        _phoneNumber = phoneNumber;
        return this;
    }

    public UserBuilder WithFullName(string fullName)
    {
        _fullName = fullName;
        return this;
    }

    public UserBuilder WithEmail(string email)
    {
        _email = email;
        return this;
    }

    public UserBuilder WithRole(string role)
    {
        _role = role;
        return this;
    }

    public UserBuilder WithCreatedAt(DateTime createdAtUtc)
    {
        _createdAtUtc = createdAtUtc;
        return this;
    }

    public User Build()
    {
        if (_role == UserRole.Player)
        {
            return User.CreatePlayer(_id, _phoneNumber, _fullName, _createdAtUtc);
        }

        return User.CreateStaff(
            _id,
            _email ?? $"user_{_id:N}@malabak.app",
            _phoneNumber,
            "AQAAAAIAAYagAAAAEPwd+5...",
            _fullName,
            _role ?? UserRole.Admin,
            _createdAtUtc);
    }
}
