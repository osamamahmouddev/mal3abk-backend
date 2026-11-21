using Malabak.Modules.Identity.Application.AdminAuth;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Identity.Application.Admin;

public static class AdminUserStatus
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";
    public const string Deleted = "Deleted";

    public static string Of(User user)
    {
        if (user.IsDeleted)
        {
            return Deleted;
        }

        return user.IsSuspended ? Suspended : Active;
    }
}

public sealed record AdminUserSearch(string? Query, string? Role, string? Status, int Page = 1, int PageSize = 25);

/// <summary>List row. The phone is masked: full contact details are only on the detail view, which is audited.</summary>
public sealed record AdminUserListItem(
    Guid Id, string FullName, string MaskedPhone, string? Email, IReadOnlyList<string> Roles, string Status, DateTime CreatedAtUtc);

public sealed record AdminUserPage(IReadOnlyList<AdminUserListItem> Items, int Page, int PageSize, int Total);

public sealed record AdminUserDetail(
    Guid Id,
    string FullName,
    string PhoneNumber,
    string? Email,
    IReadOnlyList<string> Roles,
    string Status,
    string? SuspensionReason,
    DateTime? SuspendedAtUtc,
    DateTime? DeletionRequestedAtUtc,
    string? City,
    string? Area,
    string Language,
    bool TwoFactorEnabled,
    bool IsPhoneNumberConfirmed,
    bool IsEmailConfirmed,
    int ActiveSessions,
    DateTime? LastActivityAtUtc,
    DateTime CreatedAtUtc);

public static class AdminUserErrors
{
    public static readonly Error NotFound = Error.NotFound("AdminUsers.NotFound", "User not found.");
    public static readonly Error ReasonRequired = Error.Validation("AdminUsers.ReasonRequired", "A reason is required for this action.");
    public static readonly Error InvalidFilter = Error.Validation("AdminUsers.InvalidFilter", "Invalid search filter.");
    public static readonly Error ReviewAccountProtected = Error.Forbidden("AdminUsers.ReviewAccountProtected",
        "This is the App Store / Google Play review account: it cannot be suspended or deleted, or the app review fails.");
    public static readonly Error CannotActOnSelf = Error.Forbidden("AdminUsers.CannotActOnSelf", "You cannot perform this action on your own account.");
    public static readonly Error AdminAccount = Error.Forbidden("AdminUsers.AdminAccount",
        "Platform staff accounts are managed from admin accounts, not user management.");
    public static readonly Error Deleted = Error.Conflict("AdminUsers.Deleted", "The account is deleted.");
    public static readonly Error AlreadySuspended = Error.Conflict("AdminUsers.AlreadySuspended", "The account is already suspended.");
    public static readonly Error NotSuspended = Error.Conflict("AdminUsers.NotSuspended", "The account is not suspended.");
}

/// <summary>
/// Admin user management (TASK-701): search, profile and status, suspend/reactivate (revokes every session immediately).
/// Every change is written to the audit trail with the admin's reason.
/// </summary>
public sealed class AdminUserService
{
    public const int MaxPageSize = 100;
    public const int MaxReasonLength = 500;

    private readonly IdentityDbContext _dbContext;
    private readonly SessionService _sessions;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public AdminUserService(IdentityDbContext dbContext, SessionService sessions, IAuditLog audit, IClock clock)
    {
        _dbContext = dbContext;
        _sessions = sessions;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<AdminUserPage>> SearchAsync(AdminUserSearch search, CancellationToken cancellationToken = default)
    {
        if (search.Page < 1 || search.PageSize is < 1 or > MaxPageSize || search.Query?.Length > 100
            || (search.Role is not null && !UserRole.All.Contains(search.Role, StringComparer.OrdinalIgnoreCase))
            || (search.Status is not null && search.Status is not (AdminUserStatus.Active or AdminUserStatus.Suspended or AdminUserStatus.Deleted)))
        {
            return Result.Failure<AdminUserPage>(AdminUserErrors.InvalidFilter);
        }

        var users = UserQueries.WithRole(_dbContext, search.Role).IgnoreQueryFilters().AsNoTracking();
        users = search.Status switch
        {
            AdminUserStatus.Active => users.Where(u => !u.IsDeleted && u.SuspendedAtUtc == null),
            AdminUserStatus.Suspended => users.Where(u => !u.IsDeleted && u.SuspendedAtUtc != null),
            AdminUserStatus.Deleted => users.Where(u => u.IsDeleted),
            _ => users
        };

        if (!string.IsNullOrWhiteSpace(search.Query))
        {
            var term = search.Query.Trim();
            var lowered = term.ToLowerInvariant();
            users = Guid.TryParse(term, out var id)
                ? users.Where(u => u.Id == id)
                : users.Where(u => u.FullName.ToLower().Contains(lowered) || u.PhoneNumber.Contains(term) || (u.Email != null && u.Email.Contains(lowered)));
        }

        var total = await users.CountAsync(cancellationToken);
        var rows = await users.OrderByDescending(u => u.CreatedAtUtc)
            .Skip((search.Page - 1) * search.PageSize).Take(search.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(u => new AdminUserListItem(
            u.Id, u.FullName, PhoneNumberMasker.Mask(u.PhoneNumber), u.Email, u.Roles.ToList(), AdminUserStatus.Of(u), u.CreatedAtUtc)).ToList();
        return Result.Success(new AdminUserPage(items, search.Page, search.PageSize, total));
    }

    public async Task<Result<AdminUserDetail>> GetAsync(Guid userId, Guid adminId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<AdminUserDetail>(AdminUserErrors.NotFound);
        }

        var sessions = await _dbContext.UserSessions.AsNoTracking().Where(s => s.UserId == userId && !s.IsRevoked)
            .Select(s => s.LastActivityAtUtc).ToListAsync(cancellationToken);

        // Viewing full contact details of a user is itself recorded (who looked at whom).
        await _audit.RecordAsync(new AuditEntry("admin.user.viewed", "User", userId.ToString("D"), adminId, AuditActorType.Admin), cancellationToken);

        return Result.Success(new AdminUserDetail(
            user.Id, user.FullName, user.PhoneNumber, user.Email, user.Roles.ToList(), AdminUserStatus.Of(user), user.SuspensionReason,
            user.SuspendedAtUtc, user.DeletionRequestedAtUtc, user.City, user.Area, user.Language, user.TwoFactorEnabled,
            user.IsPhoneNumberConfirmed, user.IsEmailConfirmed, sessions.Count, sessions.Count == 0 ? null : sessions.Max(), user.CreatedAtUtc));
    }

    public async Task<Result<AdminUserDetail>> SuspendAsync(Guid userId, string? reason, Guid adminId, CancellationToken cancellationToken = default)
    {
        var (user, error) = await LoadForChangeAsync(userId, reason, adminId, cancellationToken);
        if (error is not null)
        {
            return Result.Failure<AdminUserDetail>(error);
        }

        if (ReviewAccount.Is(user!.PhoneNumber))
        {
            return Result.Failure<AdminUserDetail>(AdminUserErrors.ReviewAccountProtected);
        }

        if (user.IsSuspended)
        {
            return Result.Failure<AdminUserDetail>(AdminUserErrors.AlreadySuspended);
        }

        user.Suspend(reason!, _clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        var revoked = await _sessions.RevokeAllAsync(user.Id, null, "admin_suspended", cancellationToken);

        await _audit.RecordAsync(new AuditEntry("admin.user.suspended", "User", user.Id.ToString("D"), adminId, AuditActorType.Admin,
            Before: new { Status = AdminUserStatus.Active }, After: new { Status = AdminUserStatus.Suspended, RevokedSessions = revoked }, Reason: reason), cancellationToken);
        return await GetAsync(user.Id, adminId, cancellationToken);
    }

    public async Task<Result<AdminUserDetail>> ReactivateAsync(Guid userId, string? reason, Guid adminId, CancellationToken cancellationToken = default)
    {
        var (user, error) = await LoadForChangeAsync(userId, reason, adminId, cancellationToken);
        if (error is not null)
        {
            return Result.Failure<AdminUserDetail>(error);
        }

        if (!user!.IsSuspended)
        {
            return Result.Failure<AdminUserDetail>(AdminUserErrors.NotSuspended);
        }

        var previousReason = user.SuspensionReason;
        user.Reactivate(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(new AuditEntry("admin.user.reactivated", "User", user.Id.ToString("D"), adminId, AuditActorType.Admin,
            Before: new { Status = AdminUserStatus.Suspended, Reason = previousReason }, After: new { Status = AdminUserStatus.Active }, Reason: reason), cancellationToken);
        return await GetAsync(user.Id, adminId, cancellationToken);
    }

    /// <summary>Signs the user out everywhere (e.g. a reported account takeover) without suspending it.</summary>
    public async Task<Result<int>> RevokeSessionsAsync(Guid userId, string? reason, Guid adminId, CancellationToken cancellationToken = default)
    {
        var (user, error) = await LoadForChangeAsync(userId, reason, adminId, cancellationToken);
        if (error is not null)
        {
            return Result.Failure<int>(error);
        }

        var revoked = await _sessions.RevokeAllAsync(user!.Id, null, "admin_revoked", cancellationToken);
        await _audit.RecordAsync(new AuditEntry("admin.user.sessions_revoked", "User", user.Id.ToString("D"), adminId, AuditActorType.Admin,
            After: new { RevokedSessions = revoked }, Reason: reason), cancellationToken);
        return Result.Success(revoked);
    }

    private async Task<(User? User, Error? Error)> LoadForChangeAsync(Guid userId, string? reason, Guid adminId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaxReasonLength)
        {
            return (null, AdminUserErrors.ReasonRequired);
        }

        if (userId == adminId)
        {
            return (null, AdminUserErrors.CannotActOnSelf);
        }

        var user = await _dbContext.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return (null, AdminUserErrors.NotFound);
        }

        if (user.IsDeleted)
        {
            return (null, AdminUserErrors.Deleted);
        }

        // Staff accounts have their own screen (TASK-713) with the last-admin safeguards.
        return user.Roles.Any(AdminAuthHandlers.AdminRoles.Contains) ? (null, AdminUserErrors.AdminAccount) : (user, null);
    }
}

internal static class UserQueries
{
    /// <summary>
    /// Users holding a role. Roles are a JSON array column, so on PostgreSQL the filter is a text match on the quoted role
    /// name (role names are a fixed allow-list, never user text); other providers filter in memory (tests only).
    /// </summary>
    public static IQueryable<User> WithRole(IdentityDbContext dbContext, string? role)
    {
        if (role is null)
        {
            return dbContext.Users;
        }

        var canonical = UserRole.All.First(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
        if (dbContext.Database.IsRelational())
        {
            var pattern = $"%\"{canonical}\"%";
            return dbContext.Users.FromSqlInterpolated($"SELECT *, xmin FROM identity.users WHERE \"Roles\" LIKE {pattern}");
        }

        var ids = dbContext.Users.IgnoreQueryFilters().AsNoTracking().AsEnumerable()
            .Where(u => u.Roles.Contains(canonical, StringComparer.OrdinalIgnoreCase)).Select(u => u.Id).ToList();
        return dbContext.Users.Where(u => ids.Contains(u.Id));
    }
}
