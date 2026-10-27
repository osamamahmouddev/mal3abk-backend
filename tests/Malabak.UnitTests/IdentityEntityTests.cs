using FluentAssertions;
using Malabak.Modules.Identity.Domain;
using Xunit;

namespace Malabak.UnitTests;

public sealed class IdentityEntityTests
{
    [Fact]
    public void CreatePlayer_ShouldInitializeWithPlayerRoleAndConfirmedPhone()
    {
        // Arrange
        var id = Guid.NewGuid();
        var phone = "+201012345678";
        var name = "Ahmed Ali";
        var now = DateTime.UtcNow;

        // Act
        var player = User.CreatePlayer(id, phone, name, now);

        // Assert
        player.Id.Should().Be(id);
        player.PhoneNumber.Should().Be(phone);
        player.FullName.Should().Be(name);
        player.IsPhoneNumberConfirmed.Should().BeTrue();
        player.SecurityStamp.Should().NotBeNullOrWhiteSpace();
        player.Roles.Should().Contain(UserRole.Player);
        player.CreatedAtUtc.Should().Be(now);
    }

    [Fact]
    public void CreateStaff_ShouldInitializeWithStaffRoleAndConfirmedEmail()
    {
        // Arrange
        var id = Guid.NewGuid();
        var email = "admin@malabak.com";
        var phone = "+201098765432";
        var passHash = "hashed_pass";
        var name = "Staff Admin";
        var now = DateTime.UtcNow;

        // Act
        var staff = User.CreateStaff(id, email, phone, passHash, name, UserRole.Admin, now);

        // Assert
        staff.Id.Should().Be(id);
        staff.Email.Should().Be(email);
        staff.Roles.Should().Contain(UserRole.Admin);
        staff.IsEmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public void ChangePhoneNumber_ShouldUpdatePhoneAndRotateSecurityStamp()
    {
        // Arrange
        var player = User.CreatePlayer(Guid.NewGuid(), "+201011111111", "Player One", DateTime.UtcNow);
        var oldStamp = player.SecurityStamp;
        var newPhone = "+201022222222";
        var updateTime = DateTime.UtcNow.AddMinutes(5);

        // Act
        player.ChangePhoneNumber(newPhone, updateTime);

        // Assert
        player.PhoneNumber.Should().Be(newPhone);
        player.SecurityStamp.Should().NotBe(oldStamp);
        player.LastPhoneChangeAtUtc.Should().Be(updateTime);
    }

    [Fact]
    public void RefreshToken_CreateAndRevoke_ShouldTrackStateProperly()
    {
        // Arrange
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var tokenHash = "sha256_dummy_hash";
        var now = DateTime.UtcNow;
        var expires = now.AddDays(7);

        // Act
        var token = RefreshToken.Create(id, userId, tokenHash, familyId, expires, now);

        // Assert
        token.IsActive(now).Should().BeTrue();
        token.IsRevoked.Should().BeFalse();

        // Act: Revoke
        var revokeTime = now.AddHours(1);
        token.Revoke("sha256_new_token_hash", revokeTime);

        // Assert
        token.IsRevoked.Should().BeTrue();
        token.IsActive(now).Should().BeFalse();
        token.ReplacedByTokenHash.Should().Be("sha256_new_token_hash");
    }

    [Fact]
    public void PhoneChangeRequest_StateTransitions_ShouldFollowSequence()
    {
        // Arrange
        var req = PhoneChangeRequest.Start(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "+201011111111",
            "+201022222222",
            "old_hash",
            DateTime.UtcNow.AddMinutes(15),
            DateTime.UtcNow);

        req.Status.Should().Be(PhoneChangeStatus.Started);

        // Act: verify old
        req.MarkOldPhoneVerified("new_hash", DateTime.UtcNow);
        req.Status.Should().Be(PhoneChangeStatus.OldVerified);

        // Act: complete
        req.Complete("recovery_token_hash", DateTime.UtcNow);
        req.Status.Should().Be(PhoneChangeStatus.Completed);
        req.CompletedAtUtc.Should().NotBeNull();
    }
}
