using FluentAssertions;
using Malabak.Api.Endpoints;
using Malabak.Modules.Identity.Application.Admin;
using Malabak.Modules.Identity.Application.Authorization;
using Malabak.Modules.Identity.Application.PasswordAuth;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class AdminUserTests
{
    private static readonly DateTime _now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    private readonly Clock _clock = new();
    private readonly AuditRecorder _audit = new();
    private readonly ResetRecorder _reset = new();

    [Fact]
    public async Task Search_FiltersByTextRoleAndStatus_AndMasksPhonesAsync()
    {
        await using var db = NewDb();
        var ahmed = Player(db, "Ahmed Ali", "+201012345678");
        Player(db, "Mona Samir", "+201112345678");
        var owner = Staff(db, "Karim Owner", "karim@example.com", UserRole.VenueOwner);
        ahmed.Suspend("Abuse", _now);
        await db.SaveChangesAsync();
        var service = Users(db);

        var byName = (await service.SearchAsync(new AdminUserSearch("ahmed", null, null))).Value;
        byName.Items.Should().ContainSingle().Which.MaskedPhone.Should().Be("+2010•••••678");
        (await service.SearchAsync(new AdminUserSearch(null, UserRole.VenueOwner, null))).Value.Items.Should().ContainSingle(u => u.Id == owner.Id);
        (await service.SearchAsync(new AdminUserSearch(null, null, AdminUserStatus.Suspended))).Value.Items.Should().ContainSingle(u => u.Id == ahmed.Id);
        (await service.SearchAsync(new AdminUserSearch(null, null, null, PageSize: 2))).Value.Should().Match<AdminUserPage>(p => p.Total == 3 && p.Items.Count == 2);
        (await service.SearchAsync(new AdminUserSearch(null, "Hacker", null))).Error.Should().Be(AdminUserErrors.InvalidFilter);
    }

    [Fact]
    public async Task Suspend_BlocksPermissions_RevokesSessions_AndIsAuditedAsync()
    {
        await using var db = NewDb();
        var player = Player(db, "Ahmed", "+201012345678");
        await db.SaveChangesAsync();
        var sessions = TestSessions.Create(db, _clock);
        await sessions.StartAsync(player, isAdminSession: false);
        var service = Users(db);
        var admin = Guid.NewGuid();

        (await service.SuspendAsync(player.Id, " ", admin)).Error.Should().Be(AdminUserErrors.ReasonRequired);
        var detail = (await service.SuspendAsync(player.Id, "Repeated no-shows", admin)).Value;

        detail.Status.Should().Be(AdminUserStatus.Suspended);
        detail.ActiveSessions.Should().Be(0);
        (await new UserPermissionService(db).GetAsync(player.Id)).Should().BeNull("a suspended account has no permissions on its next request");
        _audit.Entries.Should().Contain(e => e.Action == "admin.user.suspended" && e.Reason == "Repeated no-shows" && e.ActorId == admin);
        (await service.SuspendAsync(player.Id, "again", admin)).Error.Should().Be(AdminUserErrors.AlreadySuspended);

        (await service.ReactivateAsync(player.Id, "Appeal accepted", admin)).Value.Status.Should().Be(AdminUserStatus.Active);
        (await new UserPermissionService(db).GetAsync(player.Id))!.Has(Permissions.BookingsCreate).Should().BeTrue();
    }

    [Fact]
    public async Task TheAppReviewAccount_CannotBeSuspendedAsync()
    {
        await using var db = NewDb();
        var reviewer = Player(db, "Apple Reviewer", "+201000000999");
        await db.SaveChangesAsync();

        var result = await Users(db).SuspendAsync(reviewer.Id, "Looks like a test account", Guid.NewGuid());

        result.Error.Should().Be(AdminUserErrors.ReviewAccountProtected);
        (await db.Users.SingleAsync(u => u.Id == reviewer.Id)).IsSuspended.Should().BeFalse();
    }

    [Fact]
    public async Task UserManagement_CannotTouchStaffAccountsOrSelfAsync()
    {
        await using var db = NewDb();
        var admin = Staff(db, "Root", "root@example.com", UserRole.Admin);
        var support = Staff(db, "Help", "help@example.com", UserRole.SupportAgent);
        await db.SaveChangesAsync();
        var service = Users(db);

        (await service.SuspendAsync(admin.Id, "x", admin.Id)).Error.Should().Be(AdminUserErrors.CannotActOnSelf);
        (await service.SuspendAsync(support.Id, "x", admin.Id)).Error.Should().Be(AdminUserErrors.AdminAccount);
        (await service.SuspendAsync(Guid.NewGuid(), "x", admin.Id)).Error.Should().Be(AdminUserErrors.NotFound);
    }

    [Fact]
    public async Task ViewingAUser_ShowsFullContact_AndRecordsWhoLookedAsync()
    {
        await using var db = NewDb();
        var player = Player(db, "Ahmed", "+201012345678");
        await db.SaveChangesAsync();
        var admin = Guid.NewGuid();

        var detail = (await Users(db).GetAsync(player.Id, admin)).Value;

        detail.PhoneNumber.Should().Be("+201012345678");
        _audit.Entries.Should().ContainSingle(e => e.Action == "admin.user.viewed" && e.EntityId == player.Id.ToString("D") && e.ActorId == admin);
    }

    [Fact]
    public async Task CreatingStaff_GivesNoUsablePassword_SendsAResetLink_AndRejectsNonStaffRolesAsync()
    {
        await using var db = NewDb();
        var root = Staff(db, "Root", "root@example.com", UserRole.Admin);
        await db.SaveChangesAsync();
        var service = Accounts(db);

        (await service.CreateAsync(new CreateAdminAccountInput("Eve", "eve@example.com", null, [UserRole.VenueOwner]), root.Id)).Error
            .Should().Be(AdminAccountErrors.InvalidRoles);
        (await service.CreateAsync(new CreateAdminAccountInput("Dup", "ROOT@example.com", null, [UserRole.Finance]), root.Id)).Error
            .Should().Be(AdminAccountErrors.EmailInUse);

        var created = (await service.CreateAsync(new CreateAdminAccountInput("Sara", "Sara@Example.com", null, ["supportagent", "Finance"]), root.Id)).Value;

        created.Email.Should().Be("sara@example.com");
        created.Roles.Should().BeEquivalentTo(UserRole.SupportAgent, UserRole.Finance);
        created.TwoFactorEnabled.Should().BeFalse();
        _reset.Emails.Should().ContainSingle("sara@example.com");
        _audit.Entries.Should().Contain(e => e.Action == "admin.account.created");
        (await service.ListAsync()).Select(a => a.Email).Should().BeEquivalentTo("root@example.com", "sara@example.com");
    }

    [Fact]
    public async Task TheLastActiveAdmin_CanNeverBeRemoved_AndAdminsCannotDemoteThemselvesAsync()
    {
        await using var db = NewDb();
        var root = Staff(db, "Root", "root@example.com", UserRole.Admin);
        var second = Staff(db, "Second", "second@example.com", UserRole.Admin);
        await db.SaveChangesAsync();
        var service = Accounts(db);

        (await service.ReplaceRolesAsync(root.Id, [UserRole.Finance], "self", root.Id)).Error.Should().Be(AdminAccountErrors.CannotChangeSelf);
        (await service.DisableAsync(root.Id, "self", root.Id)).Error.Should().Be(AdminAccountErrors.CannotChangeSelf);

        (await service.ReplaceRolesAsync(second.Id, [UserRole.Finance], "moved to finance", root.Id)).Value.Roles.Should().Equal(UserRole.Finance);
        (await new UserPermissionService(db).GetAsync(second.Id))!.Has(Permissions.AdminUsersManage).Should().BeFalse("role changes apply immediately");

        var third = Staff(db, "Third", "third@example.com", UserRole.Admin);
        await db.SaveChangesAsync();
        (await service.DisableAsync(third.Id, "left the company", root.Id)).Value.Status.Should().Be(AdminUserStatus.Suspended);
        // root is now the only active Admin; a second session of root acting on itself is refused, and nobody else can remove it.
        (await service.ReplaceRolesAsync(root.Id, [UserRole.SupportAgent], "x", third.Id)).Error.Should().Be(AdminAccountErrors.LastAdmin);
        (await service.DisableAsync(root.Id, "x", third.Id)).Error.Should().Be(AdminAccountErrors.LastAdmin);
    }

    [Fact]
    public void RolesCatalog_ListsStaffRolesFirst_AndNavigationFollowsPermissions()
    {
        var roles = AdminAccountService.Roles();
        roles.Take(3).Should().OnlyContain(r => r.IsStaffRole);
        roles.Single(r => r.Role == UserRole.Admin).Permissions.Should().Contain(Permissions.AdminAccountsManage);
        roles.Single(r => r.Role == UserRole.SupportAgent).Permissions.Should().NotContain(Permissions.AdminUsersManage).And.Contain(Permissions.AdminUsersView);

        var support = new UserPermissions(Guid.NewGuid(), [UserRole.SupportAgent], RolePermissions.For([UserRole.SupportAgent]));
        AdminNavigation.For(support).Select(n => n.Key).Should().BeEquivalentTo("dashboard", "users", "bookings", "support");

        // Money the platform holds (deposits, refunds, venue payouts) is for admins and finance, not support.
        var finance = new UserPermissions(Guid.NewGuid(), [UserRole.Finance], RolePermissions.For([UserRole.Finance]));
        AdminNavigation.For(finance).Select(n => n.Key).Should().BeEquivalentTo("dashboard", "bookings", "deposits", "payouts", "invoices");
        AdminNavigation.For(null).Should().BeEmpty();
    }

    [Fact]
    public void SuspendedUsers_CannotSignIn_ButKeepTheirData()
    {
        var user = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Ahmed", _now);
        var stamp = user.SecurityStamp;

        user.Suspend(new string('x', 900), _now);

        user.IsSuspended.Should().BeTrue();
        user.SuspensionReason.Should().HaveLength(User.MaxSuspensionReasonLength);
        user.SecurityStamp.Should().NotBe(stamp, "existing tokens are invalidated");
        user.IsDeleted.Should().BeFalse();
    }

    private AdminUserService Users(IdentityDbContext db) => new(db, TestSessions.Create(db, _clock), _audit, _clock);

    private AdminAccountService Accounts(IdentityDbContext db) =>
        new(db, new Pbkdf2PasswordHasher(), new PhoneNumberNormalizer(), _reset, TestSessions.Create(db, _clock), _audit, _clock);

    private static User Player(IdentityDbContext db, string name, string phone)
    {
        var user = User.CreatePlayer(Guid.NewGuid(), phone, name, _now);
        db.Users.Add(user);
        return user;
    }

    private static User Staff(IdentityDbContext db, string name, string email, string role)
    {
        var user = User.CreateStaff(Guid.NewGuid(), email, Guid.NewGuid().ToString("N"), "hash", name, role, _now);
        db.Users.Add(user);
        return user;
    }

    private static IdentityDbContext NewDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class AuditRecorder : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class ResetRecorder : ICommandHandler<ForgotPasswordCommand, Result>
    {
        public List<string> Emails { get; } = [];

        public Task<Result> HandleAsync(ForgotPasswordCommand command, CancellationToken cancellationToken = default)
        {
            Emails.Add(command.Email);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class Clock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        public DateTime UtcNow { get; set; } = _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
