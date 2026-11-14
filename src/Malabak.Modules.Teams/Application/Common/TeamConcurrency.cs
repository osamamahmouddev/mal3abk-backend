using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Malabak.Modules.Teams.Application.Common;

/// <summary>
/// Serialises the use cases that add a player to a team, so the per-player team limit and the per-team roster cap are
/// counted and written atomically. The advisory locks live until the surrounding transaction ends.
/// Lock order is always player first, then team: two requests can then never wait on each other.
/// </summary>
internal static class TeamConcurrency
{
    /// <summary>The unique index on <c>teams.invite_code</c> (see <c>TeamConfiguration</c>).</summary>
    public const string InviteCodeIndex = "IX_teams_InviteCode";

    /// <summary>The unique index on <c>teams.slug</c>.</summary>
    public const string SlugIndex = "IX_teams_Slug";

    private const string _uniqueViolation = "23505";

    /// <summary>Starts a transaction on a relational provider; the in-memory test provider has none and returns null.</summary>
    public static async Task<IDbContextTransaction?> BeginAsync(ITeamsDbContext db, CancellationToken cancellationToken) =>
        db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;

    /// <summary>One membership change per player at a time (the 5-teams limit).</summary>
    public static Task LockPlayerAsync(ITeamsDbContext db, Guid playerId, CancellationToken cancellationToken) =>
        LockAsync(db, $"teams:player:{playerId:N}", cancellationToken);

    /// <summary>One membership change per team at a time (the 25-member roster cap, disbanding).</summary>
    public static Task LockTeamAsync(ITeamsDbContext db, Guid teamId, CancellationToken cancellationToken) =>
        LockAsync(db, $"teams:team:{teamId:N}", cancellationToken);

    /// <summary>A generated invite code or slug already exists on another team: retry with a new one.</summary>
    public static bool IsCodeOrSlugCollision(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: _uniqueViolation, ConstraintName: InviteCodeIndex or SlugIndex };

    private static Task LockAsync(ITeamsDbContext db, string key, CancellationToken cancellationToken) =>
        db.Database.IsRelational()
            ? db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken)
            : Task.CompletedTask;
}
