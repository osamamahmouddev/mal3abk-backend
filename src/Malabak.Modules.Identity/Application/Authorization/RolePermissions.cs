using Malabak.Modules.Identity.Domain;
using Malabak.SharedKernel.Authorization;

namespace Malabak.Modules.Identity.Application.Authorization;

/// <summary>
/// Maps platform roles to platform permissions (least privilege). Venue-scoped permissions are never
/// granted by role alone — a VenueOwner/VenueStaff still needs a relationship with the specific venue.
/// </summary>
public static class RolePermissions
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> _map =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [UserRole.Player] = [Permissions.ProfileManage, Permissions.BookingsCreate, Permissions.TeamsParticipate],
            [UserRole.VenueOwner] = [Permissions.ProfileManage, Permissions.VenuesManage],
            [UserRole.VenueStaff] = [Permissions.ProfileManage, Permissions.VenuesManage],
            [UserRole.Admin] =
            [
                Permissions.ProfileManage,
                Permissions.AdminUsersView,
                Permissions.AdminUsersManage,
                Permissions.AdminAccountsManage,
                Permissions.AdminVenuesApprove,
                Permissions.AdminBookingsView,
                Permissions.AdminDisputesResolve,
                Permissions.AdminSettingsManage,
                Permissions.AuditLogView,
                Permissions.SupportTicketsManage,
                Permissions.SubscriptionsManage,
                Permissions.InvoicesView
            ],
            [UserRole.SupportAgent] =
            [
                Permissions.ProfileManage,
                Permissions.SupportTicketsManage,
                Permissions.AdminUsersView,
                Permissions.AdminBookingsView
            ],
            [UserRole.Finance] =
            [
                Permissions.ProfileManage,
                Permissions.SubscriptionsManage,
                Permissions.InvoicesView
            ]
        };

    /// <summary>The full role → permissions catalog (admin roles screen, TASK-713).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> Catalog => _map;

    public static IReadOnlySet<string> For(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var permissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            if (_map.TryGetValue(role, out var granted))
            {
                permissions.UnionWith(granted);
            }
        }

        return permissions;
    }
}
