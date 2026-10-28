using FluentAssertions;
using Malabak.Modules.Identity.Application.Authorization;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class AuthorizationTests
{
    private static readonly DateTime _now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RolePermissions_Player_GetsPlayerPermissionsOnly()
    {
        var permissions = RolePermissions.For([UserRole.Player]);

        permissions.Should().BeEquivalentTo(Permissions.ProfileManage, Permissions.BookingsCreate, Permissions.TeamsParticipate);
        permissions.Should().NotContain(Permissions.VenuesManage);
    }

    [Fact]
    public void RolePermissions_VenueOwner_CanEnterVenueManagementButHasNoAdminPermissions()
    {
        var permissions = RolePermissions.For([UserRole.VenueOwner]);

        permissions.Should().Contain(Permissions.VenuesManage);
        permissions.Should().NotContain(p => p.StartsWith("admin.", StringComparison.Ordinal));
    }

    [Fact]
    public void RolePermissions_Admin_GetsAdminPermissionsButNotVenueManagement()
    {
        var permissions = RolePermissions.For([UserRole.Admin]);

        permissions.Should().Contain([Permissions.AdminUsersManage, Permissions.AdminVenuesApprove, Permissions.AuditLogView]);
        permissions.Should().NotContain(Permissions.VenuesManage);
    }

    [Fact]
    public void RolePermissions_MultipleRoles_AreUnioned_AndUnknownRolesIgnored()
    {
        var permissions = RolePermissions.For([UserRole.Player, UserRole.VenueOwner, "Hacker"]);

        permissions.Should().Contain([Permissions.BookingsCreate, Permissions.VenuesManage]);
        RolePermissions.For(["Hacker"]).Should().BeEmpty();
    }

    [Fact]
    public void RolePermissions_NoRole_GrantsVenueScopedPermissions()
    {
        foreach (var role in UserRole.All)
        {
            RolePermissions.For([role]).Should().NotIntersectWith(Permissions.VenueScoped, $"role {role} must not grant venue-scoped permissions");
        }
    }

    [Fact]
    public void PermissionCatalog_HasNoDuplicatesAcrossPlatformAndVenueScopes()
    {
        var all = Permissions.Platform.Concat(Permissions.VenueScoped).ToList();

        all.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task UserPermissionService_ReadsRolesFromDatabaseAsync()
    {
        await using var db = CreateIdentityDbContext();
        var owner = User.CreateStaff(Guid.NewGuid(), "owner@example.com", "+201001112222", "hash", "Owner", UserRole.VenueOwner, _now);
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var result = await new UserPermissionService(db).GetAsync(owner.Id);

        result.Should().NotBeNull();
        result!.Roles.Should().Equal(UserRole.VenueOwner);
        result.Has(Permissions.VenuesManage).Should().BeTrue();
        result.Has(Permissions.AdminUsersManage).Should().BeFalse();
    }

    [Fact]
    public async Task UserPermissionService_RoleRemovedInDatabase_IsRevokedImmediatelyAsync()
    {
        await using var db = CreateIdentityDbContext();
        var owner = User.CreateStaff(Guid.NewGuid(), "owner@example.com", "+201001112222", "hash", "Owner", UserRole.VenueOwner, _now);
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        owner.RemoveRole(UserRole.VenueOwner);
        await db.SaveChangesAsync();

        var result = await new UserPermissionService(db).GetAsync(owner.Id);

        result!.Has(Permissions.VenuesManage).Should().BeFalse();
    }

    [Fact]
    public async Task UserPermissionService_UnknownUser_ReturnsNullAsync()
    {
        await using var db = CreateIdentityDbContext();

        (await new UserPermissionService(db).GetAsync(Guid.NewGuid())).Should().BeNull();
    }

    private static IdentityDbContext CreateIdentityDbContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
