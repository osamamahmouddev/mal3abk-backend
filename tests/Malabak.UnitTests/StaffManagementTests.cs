using FluentAssertions;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Pitches.CreatePitch;
using Malabak.Modules.Venues.Application.Staff;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.Modules.Venues.Infrastructure.Repositories;
using Malabak.Modules.Venues.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class StaffManagementTests
{
    private readonly MutableClock _clock = new();
    private readonly FakeAccounts _accounts = new();
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _inviteeId = Guid.NewGuid();

    [Fact]
    public void RolePermissions_AreLeastPrivilege_AndNeverIncludeStaffManagement()
    {
        foreach (var role in Enum.GetValues<VenueStaffRole>())
        {
            var permissions = VenueStaffRolePermissions.For(role);
            permissions.Should().Contain(Permissions.VenueView);
            permissions.Should().NotContain([Permissions.StaffManage, Permissions.VenueDelete, Permissions.PaymentMethodsManage]);
        }

        VenueStaffRolePermissions.For(VenueStaffRole.Receptionist).Should().NotContain(Permissions.PitchesManage);
    }

    [Fact]
    public async Task Invite_Accept_GrantsStaffAccess_AndVenueStaffRoleAsync()
    {
        await using var db = NewDb();
        var venue = await SeedVenueAsync(db);
        var handlers = Handlers(db);

        var invite = await handlers.HandleAsync(new InviteStaffCommand(venue.Id, _ownerId, "Staff@Club.com", null, VenueStaffRole.Manager));
        invite.Value.Email.Should().Be("staff@club.com");
        (await db.StaffInvitations.SingleAsync()).TokenHash.Should().NotBe(_accounts.LastToken);

        _accounts.Matches = true;
        var accepted = await handlers.HandleAsync(new AcceptStaffInvitationCommand(_accounts.LastToken!, _inviteeId));

        accepted.Value.Role.Should().Be("Manager");
        _accounts.GrantedRoles.Should().Contain((_inviteeId, "VenueStaff"));
        var access = await new VenueAccessService(db).GetAccessAsync(_inviteeId, venue.Id);
        access.Level.Should().Be(VenueAccessLevel.Staff);
        access.Can(Permissions.PitchesManage).Should().BeTrue();
        access.Can(Permissions.StaffManage).Should().BeFalse();
        (await new VenueAccessService(db).GetAccessibleVenuesAsync(_inviteeId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Accept_RequiresMatchingContact_IsSingleUse_AndExpiresAfter48HoursAsync()
    {
        await using var db = NewDb();
        var venue = await SeedVenueAsync(db);
        var handlers = Handlers(db);
        await handlers.HandleAsync(new InviteStaffCommand(venue.Id, _ownerId, null, "01001234567", VenueStaffRole.Receptionist));
        var token = _accounts.LastToken!;

        _accounts.Matches = false;
        (await handlers.HandleAsync(new AcceptStaffInvitationCommand(token, _inviteeId))).Error.Should().Be(StaffErrors.InvalidInvitation);

        _accounts.Matches = true;
        _clock.UtcNow = _clock.UtcNow.AddHours(49);
        (await handlers.HandleAsync(new AcceptStaffInvitationCommand(token, _inviteeId))).Error.Should().Be(StaffErrors.InvalidInvitation);

        _clock.UtcNow = _clock.UtcNow.AddHours(-49);
        (await handlers.HandleAsync(new AcceptStaffInvitationCommand(token, _inviteeId))).IsSuccess.Should().BeTrue();
        (await handlers.HandleAsync(new AcceptStaffInvitationCommand(token, Guid.NewGuid()))).Error.Should().Be(StaffErrors.InvalidInvitation);
    }

    [Fact]
    public async Task Resend_InvalidatesOldLink_Revoke_StopsAcceptanceAsync()
    {
        await using var db = NewDb();
        var venue = await SeedVenueAsync(db);
        var handlers = Handlers(db);
        var invite = await handlers.HandleAsync(new InviteStaffCommand(venue.Id, _ownerId, "a@b.com", null, VenueStaffRole.Accountant));
        var oldToken = _accounts.LastToken!;
        _accounts.Matches = true;

        await handlers.HandleAsync(new ResendStaffInvitationCommand(venue.Id, invite.Value.InvitationId, _ownerId));
        (await handlers.HandleAsync(new AcceptStaffInvitationCommand(oldToken, _inviteeId))).IsFailure.Should().BeTrue();

        await handlers.HandleAsync(new RevokeStaffInvitationCommand(venue.Id, invite.Value.InvitationId, _ownerId));
        (await handlers.HandleAsync(new AcceptStaffInvitationCommand(_accounts.LastToken!, _inviteeId))).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task OnlyStaffManagers_CanManage_StaffCannotInviteOthersAsync()
    {
        await using var db = NewDb();
        var venue = await SeedVenueAsync(db);
        db.StaffMembers.Add(VenueStaffMember.Create(venue.Id, _inviteeId, VenueStaffRole.Manager, _ownerId, _clock.UtcNow));
        await db.SaveChangesAsync();
        var handlers = Handlers(db);

        var byStaff = await handlers.HandleAsync(new InviteStaffCommand(venue.Id, _inviteeId, "x@y.com", null, VenueStaffRole.Manager));
        var byStranger = await handlers.HandleAsync(new GetVenueStaffQuery(venue.Id, Guid.NewGuid()));

        byStaff.Error.Code.Should().Be("Venue.NotFound");
        byStranger.Error.Code.Should().Be("Venue.NotFound");
    }

    [Fact]
    public async Task Invite_ValidatesContact_RejectsDuplicates_AndEnforcesQuotaAsync()
    {
        await using var db = NewDb();
        var venue = await SeedVenueAsync(db);
        var handlers = Handlers(db);

        (await handlers.HandleAsync(new InviteStaffCommand(venue.Id, _ownerId, "a@b.com", "01001234567", VenueStaffRole.Manager))).Error
            .Should().Be(StaffErrors.InvalidContact, "exactly one contact");
        (await handlers.HandleAsync(new InviteStaffCommand(venue.Id, _ownerId, "not-an-email", null, VenueStaffRole.Manager))).Error
            .Should().Be(StaffErrors.InvalidContact);

        await handlers.HandleAsync(new InviteStaffCommand(venue.Id, _ownerId, "a@b.com", null, VenueStaffRole.Manager));
        (await handlers.HandleAsync(new InviteStaffCommand(venue.Id, _ownerId, "A@B.com", null, VenueStaffRole.Manager))).Error
            .Should().Be(StaffErrors.AlreadyInvited);

        var limited = new StaffManagementHandlers(db, new VenueAccessService(db), _accounts, new NoQuota(), new NoopAudit(), _clock);
        (await limited.HandleAsync(new InviteStaffCommand(venue.Id, _ownerId, "c@d.com", null, VenueStaffRole.Manager))).Error
            .Should().Be(StaffErrors.QuotaReached);
    }

    [Fact]
    public async Task ChangeRole_And_Remove_TakeEffectImmediatelyAsync()
    {
        await using var db = NewDb();
        var venue = await SeedVenueAsync(db);
        var member = VenueStaffMember.Create(venue.Id, _inviteeId, VenueStaffRole.Manager, _ownerId, _clock.UtcNow);
        db.StaffMembers.Add(member);
        await db.SaveChangesAsync();
        var handlers = Handlers(db);
        var access = new VenueAccessService(db);

        await handlers.HandleAsync(new ChangeStaffRoleCommand(venue.Id, member.Id, _ownerId, VenueStaffRole.Receptionist));
        (await access.GetAccessAsync(_inviteeId, venue.Id)).Can(Permissions.PitchesManage).Should().BeFalse();

        await handlers.HandleAsync(new RemoveStaffMemberCommand(venue.Id, member.Id, _ownerId));
        (await access.GetAccessAsync(_inviteeId, venue.Id)).HasAccess.Should().BeFalse();
    }

    [Fact]
    public async Task ManagerStaff_CanCreatePitch_ReceptionistCannotAsync()
    {
        await using var db = NewDb();
        var venue = await SeedVenueAsync(db);
        var receptionistId = Guid.NewGuid();
        db.StaffMembers.AddRange(
            VenueStaffMember.Create(venue.Id, _inviteeId, VenueStaffRole.Manager, _ownerId, _clock.UtcNow),
            VenueStaffMember.Create(venue.Id, receptionistId, VenueStaffRole.Receptionist, _ownerId, _clock.UtcNow));
        await db.SaveChangesAsync();
        var handler = new CreatePitchHandler(new VenueRepository(db), new PitchRepository(db), db, _clock, NullLogger<CreatePitchHandler>.Instance);

        CreatePitchCommand Cmd(Guid userId) => new(venue.Id, userId, "P", SurfaceType.ArtificialTurfValue, 5, null, null, null);

        (await handler.HandleAsync(Cmd(_inviteeId))).IsSuccess.Should().BeTrue();
        (await handler.HandleAsync(Cmd(receptionistId))).IsFailure.Should().BeTrue();
    }

    private StaffManagementHandlers Handlers(VenuesDbContext db) =>
        new(db, new VenueAccessService(db), _accounts, new DefaultStaffQuotaPolicy(), new NoopAudit(), _clock);

    private async Task<Venue> SeedVenueAsync(VenuesDbContext db)
    {
        var venue = Venue.Create(Guid.NewGuid(), "Club", "club", null, _ownerId, "Addr", "Cairo", "Maadi", null, null, "+201000000001", null, null, _clock.UtcNow);
        db.Venues.Add(venue);
        await db.SaveChangesAsync();
        return venue;
    }

    private static VenuesDbContext NewDb() =>
        new(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeAccounts : IUserAccountsContract
    {
        public bool Matches { get; set; }
        public string? LastToken { get; private set; }
        public List<(Guid, string)> GrantedRoles { get; } = [];

        public string? NormalizePhone(string phoneNumber) => phoneNumber.StartsWith("01", StringComparison.Ordinal) ? "+2" + phoneNumber : null;

        public Task<bool> MatchesContactAsync(Guid userId, string? email, string? normalizedPhone, CancellationToken cancellationToken = default) =>
            Task.FromResult(Matches);

        public Task GrantRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default)
        {
            GrantedRoles.Add((userId, role));
            return Task.CompletedTask;
        }

        public Task SendStaffInvitationAsync(string? email, string? normalizedPhone, string venueName, string role, string rawToken,
            CancellationToken cancellationToken = default)
        {
            LastToken = rawToken;
            return Task.CompletedTask;
        }
    }

    private sealed class NoQuota : IStaffQuotaPolicy
    {
        public Task<bool> CanAddAsync(Guid venueId, int currentCount, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class NoopAudit : IVenueAuditLogger
    {
        public Task LogAsync(string action, string entityName, Guid entityId, Guid userId, object? metadata = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class MutableClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
