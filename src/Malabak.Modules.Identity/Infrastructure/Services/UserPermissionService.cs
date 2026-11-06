using Malabak.Modules.Identity.Application.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Identity.Infrastructure.Services;

public sealed class UserPermissionService : IUserPermissionService
{
    private readonly IdentityDbContext _dbContext;

    public UserPermissionService(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserPermissions?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && !u.IsDeleted && u.SuspendedAtUtc == null)
            .Select(u => new { u.Id, u.Roles })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return null;
        }

        var roles = user.Roles.ToList();
        return new UserPermissions(user.Id, roles, RolePermissions.For(roles));
    }
}
