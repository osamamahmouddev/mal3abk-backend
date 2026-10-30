using System.Security.Cryptography;
using System.Text;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Staff;

public sealed record StaffMemberDto(Guid MemberId, Guid UserId, string Role, DateTime AddedAtUtc, string? FullName = null);

public sealed record StaffInvitationDto(Guid InvitationId, string? Email, string? PhoneNumber, string Role, string Status, DateTime ExpiresAtUtc);

public sealed record VenueStaffDto(IReadOnlyList<StaffMemberDto> Members, IReadOnlyList<StaffInvitationDto> PendingInvitations);

public sealed record InviteStaffCommand(Guid VenueId, Guid UserId, string? Email, string? PhoneNumber, VenueStaffRole Role) : ICommand<Result<StaffInvitationDto>>;

public sealed record ResendStaffInvitationCommand(Guid VenueId, Guid InvitationId, Guid UserId) : ICommand<Result>;

public sealed record RevokeStaffInvitationCommand(Guid VenueId, Guid InvitationId, Guid UserId) : ICommand<Result>;

public sealed record GetVenueStaffQuery(Guid VenueId, Guid UserId) : IQuery<Result<VenueStaffDto>>;

public sealed record ChangeStaffRoleCommand(Guid VenueId, Guid MemberId, Guid UserId, VenueStaffRole Role) : ICommand<Result<StaffMemberDto>>;

public sealed record RemoveStaffMemberCommand(Guid VenueId, Guid MemberId, Guid UserId) : ICommand<Result>;

public sealed record AcceptStaffInvitationCommand(string Token, Guid UserId) : ICommand<Result<StaffMemberDto>>;

public static class StaffErrors
{
    public static readonly Error InvalidContact = new("Staff.InvalidContact", "Invite by a valid email or Egyptian phone number (exactly one).");
    public static readonly Error AlreadyInvited = new("Staff.Conflict", "There is already a pending invitation for this contact.");
    public static readonly Error QuotaReached = new("Staff.QuotaReached", "This venue has reached its staff limit.");
    public static readonly Error InvitationNotFound = new("Staff.InvitationNotFound", "Invitation not found.");
    public static readonly Error MemberNotFound = new("Staff.MemberNotFound", "Staff member not found.");

    // One message for unknown, expired, revoked and "not addressed to you", so tokens can't be probed.
    public static readonly Error InvalidInvitation = new("Staff.InvalidInvitation", "This invitation is invalid, expired, or not for your account.");
}

/// <summary>Staff limit per venue. Pending TASK-060: replace with the subscription entitlement check.</summary>
public interface IStaffQuotaPolicy
{
    Task<bool> CanAddAsync(Guid venueId, int currentCount, CancellationToken cancellationToken = default);
}

public sealed class DefaultStaffQuotaPolicy : IStaffQuotaPolicy
{
    public const int MaxStaffPerVenue = 25;

    public Task<bool> CanAddAsync(Guid venueId, int currentCount, CancellationToken cancellationToken = default) =>
        Task.FromResult(currentCount < MaxStaffPerVenue);
}

/// <summary>Owner-side staff management (requires venue.staff.manage) and invitee-side acceptance.</summary>
public sealed class StaffManagementHandlers :
    ICommandHandler<InviteStaffCommand, Result<StaffInvitationDto>>,
    ICommandHandler<ResendStaffInvitationCommand, Result>,
    ICommandHandler<RevokeStaffInvitationCommand, Result>,
    IQueryHandler<GetVenueStaffQuery, Result<VenueStaffDto>>,
    ICommandHandler<ChangeStaffRoleCommand, Result<StaffMemberDto>>,
    ICommandHandler<RemoveStaffMemberCommand, Result>,
    ICommandHandler<AcceptStaffInvitationCommand, Result<StaffMemberDto>>
{
    public const string VenueStaffRoleName = "VenueStaff";

    private readonly VenuesDbContext _dbContext;
    private readonly IVenueAccessService _access;
    private readonly IUserAccountsContract _accounts;
    private readonly IStaffQuotaPolicy _quota;
    private readonly IVenueAuditLogger _audit;
    private readonly IClock _clock;

    public StaffManagementHandlers(
        VenuesDbContext dbContext,
        IVenueAccessService access,
        IUserAccountsContract accounts,
        IStaffQuotaPolicy quota,
        IVenueAuditLogger audit,
        IClock clock)
    {
        _dbContext = dbContext;
        _access = access;
        _accounts = accounts;
        _quota = quota;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<StaffInvitationDto>> HandleAsync(InviteStaffCommand command, CancellationToken cancellationToken = default)
    {
        if (!await CanManageAsync(command.UserId, command.VenueId, cancellationToken))
        {
            return Result.Failure<StaffInvitationDto>(VenueAccessErrors.VenueNotFound);
        }

        var email = NormalizeEmail(command.Email);
        var phone = string.IsNullOrWhiteSpace(command.PhoneNumber) ? null : _accounts.NormalizePhone(command.PhoneNumber);
        var hasEmail = !string.IsNullOrWhiteSpace(command.Email);
        var hasPhone = !string.IsNullOrWhiteSpace(command.PhoneNumber);
        if (hasEmail == hasPhone || (hasEmail && email is null) || (hasPhone && phone is null) || !Enum.IsDefined(command.Role))
        {
            return Result.Failure<StaffInvitationDto>(StaffErrors.InvalidContact);
        }

        var nowUtc = _clock.UtcNow;
        var duplicate = await _dbContext.StaffInvitations.AnyAsync(
            i => i.VenueId == command.VenueId && i.Status == StaffInvitationStatus.Pending && i.ExpiresAtUtc > nowUtc
                 && ((email != null && i.Email == email) || (phone != null && i.PhoneNumber == phone)),
            cancellationToken);
        if (duplicate)
        {
            return Result.Failure<StaffInvitationDto>(StaffErrors.AlreadyInvited);
        }

        var current = await CountStaffAndPendingAsync(command.VenueId, nowUtc, cancellationToken);
        if (!await _quota.CanAddAsync(command.VenueId, current, cancellationToken))
        {
            return Result.Failure<StaffInvitationDto>(StaffErrors.QuotaReached);
        }

        var (raw, hash) = NewToken();
        var invitation = VenueStaffInvitation.Create(command.VenueId, email, phone, command.Role, hash, command.UserId, nowUtc);
        _dbContext.StaffInvitations.Add(invitation);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await SendAsync(invitation, raw, cancellationToken);
        await _audit.LogAsync("Staff.Invited", nameof(VenueStaffInvitation), invitation.Id, command.UserId,
            new { invitation.VenueId, Role = invitation.Role.ToString() }, cancellationToken);
        return Result.Success(ToDto(invitation));
    }

    public async Task<Result> HandleAsync(ResendStaffInvitationCommand command, CancellationToken cancellationToken = default)
    {
        var invitation = await FindPendingInvitationAsync(command.UserId, command.VenueId, command.InvitationId, cancellationToken);
        if (invitation is null)
        {
            return Result.Failure(StaffErrors.InvitationNotFound);
        }

        var (raw, hash) = NewToken();
        invitation.Reissue(hash, _clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await SendAsync(invitation, raw, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(RevokeStaffInvitationCommand command, CancellationToken cancellationToken = default)
    {
        var invitation = await FindPendingInvitationAsync(command.UserId, command.VenueId, command.InvitationId, cancellationToken);
        if (invitation is null)
        {
            return Result.Failure(StaffErrors.InvitationNotFound);
        }

        invitation.Revoke(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Staff.InvitationRevoked", nameof(VenueStaffInvitation), invitation.Id, command.UserId, new { invitation.VenueId }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<VenueStaffDto>> HandleAsync(GetVenueStaffQuery query, CancellationToken cancellationToken = default)
    {
        if (!await CanManageAsync(query.UserId, query.VenueId, cancellationToken))
        {
            return Result.Failure<VenueStaffDto>(VenueAccessErrors.VenueNotFound);
        }

        var nowUtc = _clock.UtcNow;
        var members = await _dbContext.StaffMembers.AsNoTracking()
            .Where(m => m.VenueId == query.VenueId && !m.IsDeleted)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var invitations = await _dbContext.StaffInvitations.AsNoTracking()
            .Where(i => i.VenueId == query.VenueId && i.Status == StaffInvitationStatus.Pending && i.ExpiresAtUtc > nowUtc)
            .OrderBy(i => i.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var names = await _accounts.GetFullNamesAsync(members.Select(m => m.UserId).ToList(), cancellationToken);
        var memberDtos = members.Select(m => ToDto(m) with { FullName = names.GetValueOrDefault(m.UserId) }).ToList();
        return Result.Success(new VenueStaffDto(memberDtos, invitations.Select(ToDto).ToList()));
    }

    public async Task<Result<StaffMemberDto>> HandleAsync(ChangeStaffRoleCommand command, CancellationToken cancellationToken = default)
    {
        var member = await FindMemberAsync(command.UserId, command.VenueId, command.MemberId, cancellationToken);
        if (member is null || !Enum.IsDefined(command.Role))
        {
            return Result.Failure<StaffMemberDto>(StaffErrors.MemberNotFound);
        }

        member.ChangeRole(command.Role, _clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Staff.RoleChanged", nameof(VenueStaffMember), member.Id, command.UserId,
            new { member.VenueId, Role = member.Role.ToString() }, cancellationToken);
        return Result.Success(ToDto(member));
    }

    public async Task<Result> HandleAsync(RemoveStaffMemberCommand command, CancellationToken cancellationToken = default)
    {
        var member = await FindMemberAsync(command.UserId, command.VenueId, command.MemberId, cancellationToken);
        if (member is null)
        {
            return Result.Failure(StaffErrors.MemberNotFound);
        }

        // Access is re-checked from the database on every request, so removal takes effect immediately.
        member.Remove(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Staff.Removed", nameof(VenueStaffMember), member.Id, command.UserId, new { member.VenueId, member.UserId }, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<StaffMemberDto>> HandleAsync(AcceptStaffInvitationCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Token) || command.Token.Length > 100)
        {
            return Result.Failure<StaffMemberDto>(StaffErrors.InvalidInvitation);
        }

        var nowUtc = _clock.UtcNow;
        var hash = HashToken(command.Token.Trim());
        var invitation = await _dbContext.StaffInvitations.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
        var venue = invitation is null
            ? null
            : await _dbContext.Venues.IgnoreQueryFilters().FirstOrDefaultAsync(v => v.Id == invitation.VenueId && !v.IsDeleted, cancellationToken);

        if (invitation is null || venue is null || !invitation.IsUsable(nowUtc) || venue.OwnerId == command.UserId
            || !await _accounts.MatchesContactAsync(command.UserId, invitation.Email, invitation.PhoneNumber, cancellationToken))
        {
            return Result.Failure<StaffMemberDto>(StaffErrors.InvalidInvitation);
        }

        var member = await _dbContext.StaffMembers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.VenueId == venue.Id && m.UserId == command.UserId && !m.IsDeleted, cancellationToken);
        if (member is null)
        {
            member = VenueStaffMember.Create(venue.Id, command.UserId, invitation.Role, invitation.InvitedByUserId, nowUtc);
            _dbContext.StaffMembers.Add(member);
        }
        else
        {
            member.ChangeRole(invitation.Role, nowUtc);
        }

        invitation.Accept(command.UserId, nowUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _accounts.GrantRoleAsync(command.UserId, VenueStaffRoleName, cancellationToken);

        await _audit.LogAsync("Staff.InvitationAccepted", nameof(VenueStaffInvitation), invitation.Id, command.UserId,
            new { invitation.VenueId, Role = invitation.Role.ToString() }, cancellationToken);
        return Result.Success(ToDto(member));
    }

    private async Task<bool> CanManageAsync(Guid userId, Guid venueId, CancellationToken cancellationToken) =>
        (await _access.GetAccessAsync(userId, venueId, cancellationToken)).Can(Permissions.StaffManage);

    private async Task<VenueStaffInvitation?> FindPendingInvitationAsync(Guid userId, Guid venueId, Guid invitationId, CancellationToken cancellationToken) =>
        await CanManageAsync(userId, venueId, cancellationToken)
            ? await _dbContext.StaffInvitations.FirstOrDefaultAsync(
                i => i.Id == invitationId && i.VenueId == venueId && i.Status == StaffInvitationStatus.Pending, cancellationToken)
            : null;

    private async Task<VenueStaffMember?> FindMemberAsync(Guid userId, Guid venueId, Guid memberId, CancellationToken cancellationToken) =>
        await CanManageAsync(userId, venueId, cancellationToken)
            ? await _dbContext.StaffMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.VenueId == venueId && !m.IsDeleted, cancellationToken)
            : null;

    private async Task<int> CountStaffAndPendingAsync(Guid venueId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var members = await _dbContext.StaffMembers.CountAsync(m => m.VenueId == venueId && !m.IsDeleted, cancellationToken);
        var pending = await _dbContext.StaffInvitations.CountAsync(
            i => i.VenueId == venueId && i.Status == StaffInvitationStatus.Pending && i.ExpiresAtUtc > nowUtc, cancellationToken);
        return members + pending;
    }

    private async Task SendAsync(VenueStaffInvitation invitation, string rawToken, CancellationToken cancellationToken)
    {
        var venueName = await _dbContext.Venues.IgnoreQueryFilters()
            .Where(v => v.Id == invitation.VenueId).Select(v => v.Name).FirstAsync(cancellationToken);
        await _accounts.SendStaffInvitationAsync(invitation.Email, invitation.PhoneNumber, venueName, invitation.Role.ToString(), rawToken, cancellationToken);
    }

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256)
        {
            return null;
        }

        try
        {
            return new System.Net.Mail.MailAddress(email.Trim()).Address.ToLowerInvariant();
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static (string Raw, string Hash) NewToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (raw, HashToken(raw));
    }

    private static string HashToken(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private static StaffMemberDto ToDto(VenueStaffMember m) => new(m.Id, m.UserId, m.Role.ToString(), m.CreatedAtUtc);

    private static StaffInvitationDto ToDto(VenueStaffInvitation i) =>
        new(i.Id, i.Email, i.PhoneNumber, i.Role.ToString(), i.Status.ToString(), i.ExpiresAtUtc);
}
