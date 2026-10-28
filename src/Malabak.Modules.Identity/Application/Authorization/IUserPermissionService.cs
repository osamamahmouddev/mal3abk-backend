namespace Malabak.Modules.Identity.Application.Authorization;

public interface IUserPermissionService
{
    /// <summary>
    /// Roles and platform permissions read from the database (not the token), so a role change or
    /// account deletion takes effect on the next request. Null when the user doesn't exist or is deleted.
    /// </summary>
    Task<UserPermissions?> GetAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed record UserPermissions(Guid UserId, IReadOnlyCollection<string> Roles, IReadOnlySet<string> Permissions)
{
    public bool Has(string permission) => Permissions.Contains(permission);
}
