using System.Security.Cryptography;
using Malabak.Modules.Identity.Application.AdminAuth;
using Malabak.Modules.Identity.Application.Authorization;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.PasswordAuth;
using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Identity.Application.Admin;

public sealed record AdminAccountDto(
    Guid Id, string FullName, string Email, IReadOnlyList<string> Roles, string Status, bool TwoFactorEnabled, DateTime CreatedAtUtc);

public sealed record SystemRoleDto(string Role, bool IsStaffRole, IReadOnlyList<string> Permissions);

public sealed record CreateAdminAccountInput(string? FullName, string? Email, string? PhoneNumber, IReadOnlyList<string>? Roles);

public static class AdminAccountErrors
{
    public static readonly Error NotFound = Error.NotFound("AdminAccounts.NotFound", "Admin account not found.");
    public static readonly Error Invalid = Error.Validation("AdminAccounts.Invalid", "Enter a name, a valid email and at least one staff role.");
    public static readonly Error InvalidRoles = Error.Validation("AdminAccounts.InvalidRoles", "Only Admin, SupportAgent and Finance roles can be given here.");
    public static readonly Error EmailInUse = Error.Conflict("AdminAccounts.EmailInUse", "Another account already uses this email.");
    public static readonly Error PhoneInUse = Error.Conflict("AdminAccounts.PhoneInUse", "Another account already uses this phone number.");
    public static readonly Error CannotChangeSelf = Error.Forbidden("AdminAccounts.CannotChangeSelf",
        "You cannot disable your own account or remove your own Admin role.");
    public static readonly Error LastAdmin = Error.Conflict("AdminAccounts.LastAdmin", "At least one active Admin must remain.");
    public static readonly Error ReasonRequired = Error.Validation("AdminAccounts.ReasonRequired", "A reason is required for this action.");
    public static readonly Error AlreadyDisabled = Error.Conflict("AdminAccounts.AlreadyDisabled", "The account is already disabled.");
    public static readonly Error NotDisabled = Error.Conflict("AdminAccounts.NotDisabled", "The account is not disabled.");
}

/// <summary>
/// Platform staff accounts and system roles (TASK-713). New staff get no usable password: they receive a reset link and
/// must enrol TOTP on first sign-in. Disabling or changing roles revokes sessions so permissions apply immediately, and the
/// last active Admin can never be removed.
/// </summary>
public sealed class AdminAccountService
{
    private readonly IdentityDbContext _dbContext;
    private readonly IPasswordHasher _hasher;
    private readonly IPhoneNumberNormalizer _phones;
    private readonly ICommandHandler<ForgotPasswordCommand, Result> _passwordReset;
    private readonly SessionService _sessions;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public AdminAccountService(
        IdentityDbContext dbContext,
        IPasswordHasher hasher,
        IPhoneNumberNormalizer phones,
        ICommandHandler<ForgotPasswordCommand, Result> passwordReset,
        SessionService sessions,
        IAuditLog audit,
        IClock clock)
    {
        _dbContext = dbContext;
        _hasher = hasher;
        _phones = phones;
        _passwordReset = passwordReset;
        _sessions = sessions;
        _audit = audit;
        _clock = clock;
    }

    public static IReadOnlyList<SystemRoleDto> Roles() =>
        RolePermissions.Catalog
            .Select(r => new SystemRoleDto(r.Key, AdminAuthHandlers.AdminRoles.Contains(r.Key), r.Value.Order(StringComparer.Ordinal).ToList()))
            .OrderByDescending(r => r.IsStaffRole).ThenBy(r => r.Role, StringComparer.Ordinal)
            .ToList();

    public async Task<IReadOnlyList<AdminAccountDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var staff = await StaffQuery().AsNoTracking().OrderBy(u => u.FullName).Take(500).ToListAsync(cancellationToken);
        return staff.Select(ToDto).ToList();
    }

    public async Task<Result<AdminAccountDto>> CreateAsync(CreateAdminAccountInput input, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.FullName) || input.FullName.Trim().Length > User.MaxNameLength
            || !PasswordAuthSupport.TryNormalizeEmail(input.Email, out var email))
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.Invalid);
        }

        var roles = NormalizeRoles(input.Roles);
        if (roles is null)
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.InvalidRoles);
        }

        // Staff may sign up without a phone; the column is unique and required, so a unique non-phone placeholder is used.
        var id = Guid.CreateVersion7();
        string phone;
        if (string.IsNullOrWhiteSpace(input.PhoneNumber))
        {
            phone = id.ToString("N");
        }
        else if (_phones.Normalize(input.PhoneNumber) is { IsSuccess: true } normalized)
        {
            phone = normalized.Value;
        }
        else
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.Invalid);
        }

        if (await _dbContext.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, cancellationToken))
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.EmailInUse);
        }

        if (await _dbContext.Users.IgnoreQueryFilters().AnyAsync(u => u.PhoneNumber == phone, cancellationToken))
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.PhoneInUse);
        }

        // Nobody knows this password: the new admin sets their own through the reset link.
        var unusablePassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var user = User.CreateStaff(id, email, phone, _hasher.Hash(unusablePassword), input.FullName.Trim(), roles[0], _clock.UtcNow);
        foreach (var role in roles.Skip(1))
        {
            user.AddRole(role);
        }

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _passwordReset.HandleAsync(new ForgotPasswordCommand(email), cancellationToken);

        await _audit.RecordAsync(new AuditEntry("admin.account.created", "User", user.Id.ToString("D"), adminId, AuditActorType.Admin,
            After: new { user.FullName, Email = email, Roles = roles }), cancellationToken);
        return Result.Success(ToDto(user));
    }

    public async Task<Result<AdminAccountDto>> ReplaceRolesAsync(
        Guid accountId, IReadOnlyList<string>? newRoles, string? reason, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.ReasonRequired);
        }

        var roles = NormalizeRoles(newRoles);
        if (roles is null)
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.InvalidRoles);
        }

        var user = await StaffQuery().FirstOrDefaultAsync(u => u.Id == accountId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.NotFound);
        }

        var before = user.Roles.ToList();
        var isCurrentlyAdmin = before.Contains(UserRole.Admin, StringComparer.OrdinalIgnoreCase) || before.Contains(UserRole.SuperAdmin, StringComparer.OrdinalIgnoreCase);
        var willRemainAdmin = roles.Contains(UserRole.Admin) || roles.Contains(UserRole.SuperAdmin);
        var losesAdmin = isCurrentlyAdmin && !willRemainAdmin;
        if (losesAdmin && accountId == adminId)
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.CannotChangeSelf);
        }

        if (losesAdmin && !user.IsSuspended && await ActiveAdminCountAsync(cancellationToken) <= 1)
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.LastAdmin);
        }

        // Only staff roles are replaced; any non-staff role the person also holds (e.g. Player) is kept.
        foreach (var role in before.Where(AdminAuthHandlers.AdminRoles.Contains))
        {
            user.RemoveRole(role);
        }

        foreach (var role in roles)
        {
            user.AddRole(role);
        }

        user.UpdateSecurityStamp(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (accountId != adminId)
        {
            await _sessions.RevokeAllAsync(user.Id, null, "admin_roles_changed", cancellationToken);
        }

        await _audit.RecordAsync(new AuditEntry("admin.account.roles_replaced", "User", user.Id.ToString("D"), adminId, AuditActorType.Admin,
            Before: new { Roles = before }, After: new { Roles = user.Roles.ToList() }, Reason: reason), cancellationToken);
        return Result.Success(ToDto(user));
    }

    public async Task<Result<AdminAccountDto>> DisableAsync(Guid accountId, string? reason, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.ReasonRequired);
        }

        if (accountId == adminId)
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.CannotChangeSelf);
        }

        var user = await StaffQuery().FirstOrDefaultAsync(u => u.Id == accountId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.NotFound);
        }

        if (user.IsSuspended)
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.AlreadyDisabled);
        }

        var isAdmin = user.Roles.Contains(UserRole.Admin, StringComparer.OrdinalIgnoreCase) || user.Roles.Contains(UserRole.SuperAdmin, StringComparer.OrdinalIgnoreCase);
        if (isAdmin && await ActiveAdminCountAsync(cancellationToken) <= 1)
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.LastAdmin);
        }

        user.Suspend(reason, _clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _sessions.RevokeAllAsync(user.Id, null, "admin_account_disabled", cancellationToken);

        await _audit.RecordAsync(new AuditEntry("admin.account.disabled", "User", user.Id.ToString("D"), adminId, AuditActorType.Admin,
            After: new { Status = AdminUserStatus.Suspended }, Reason: reason), cancellationToken);
        return Result.Success(ToDto(user));
    }

    public async Task<Result<AdminAccountDto>> EnableAsync(Guid accountId, string? reason, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.ReasonRequired);
        }

        var user = await StaffQuery().FirstOrDefaultAsync(u => u.Id == accountId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.NotFound);
        }

        if (!user.IsSuspended)
        {
            return Result.Failure<AdminAccountDto>(AdminAccountErrors.NotDisabled);
        }

        user.Reactivate(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync(new AuditEntry("admin.account.enabled", "User", user.Id.ToString("D"), adminId, AuditActorType.Admin,
            After: new { Status = AdminUserStatus.Active }, Reason: reason), cancellationToken);
        return Result.Success(ToDto(user));
    }

    private IQueryable<User> StaffQuery()
    {
        // Union of all staff roles; the role filter is an allow-listed text match (see UserQueries.WithRole).
        var admins = UserQueries.WithRole(_dbContext, UserRole.Admin).Select(u => u.Id);
        var superAdmins = UserQueries.WithRole(_dbContext, UserRole.SuperAdmin).Select(u => u.Id);
        var support = UserQueries.WithRole(_dbContext, UserRole.SupportAgent).Select(u => u.Id);
        var finance = UserQueries.WithRole(_dbContext, UserRole.Finance).Select(u => u.Id);
        var ids = admins.Union(superAdmins).Union(support).Union(finance);
        return _dbContext.Users.Where(u => ids.Contains(u.Id));
    }

    private Task<int> ActiveAdminCountAsync(CancellationToken cancellationToken) =>
        _dbContext.Users.CountAsync(u => u.SuspendedAtUtc == null && !u.IsDeleted && (u.Roles.Contains(UserRole.Admin) || u.Roles.Contains(UserRole.SuperAdmin)), cancellationToken);

    private static List<string>? NormalizeRoles(IReadOnlyList<string>? roles)
    {
        if (roles is null || roles.Count == 0)
        {
            return null;
        }

        var normalized = new List<string>();
        foreach (var role in roles)
        {
            var canonical = AdminAuthHandlers.AdminRoles.FirstOrDefault(r => string.Equals(r, role?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (canonical is null)
            {
                return null;
            }

            if (!normalized.Contains(canonical))
            {
                normalized.Add(canonical);
            }
        }

        return normalized;
    }

    private static AdminAccountDto ToDto(User u) =>
        new(u.Id, u.FullName, u.Email ?? string.Empty, u.Roles.ToList(), AdminUserStatus.Of(u), u.TwoFactorEnabled, u.CreatedAtUtc);
}
