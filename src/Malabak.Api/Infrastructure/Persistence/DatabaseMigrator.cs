using Malabak.Api.Infrastructure.Media;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Matches.Infrastructure;
using Malabak.Modules.Notifications.Infrastructure;
using Malabak.Modules.Teams.Infrastructure;
using Malabak.Modules.Venues.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Api.Infrastructure.Persistence;

/// <summary>
/// The one list of module databases to migrate, shared by the <c>--migrate</c> release step and
/// <c>Database:MigrateOnStartup</c> so a new module can't be added to one and forgotten in the other.
/// </summary>
public static class DatabaseMigrator
{
    public static readonly IReadOnlyList<Type> Contexts =
    [
        typeof(IdentityDbContext),
        typeof(VenuesDbContext),
        typeof(BookingDbContext),
        typeof(TeamsDbContext),
        typeof(MatchesDbContext),
        typeof(MediaDbContext),
        typeof(NotificationsDbContext),
        typeof(SecurityDbContext),
    ];

    public static async Task MigrateAllAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        foreach (var contextType in Contexts)
        {
            var context = (DbContext)services.GetRequiredService(contextType);
            await context.Database.MigrateAsync(cancellationToken);
        }
    }
}
