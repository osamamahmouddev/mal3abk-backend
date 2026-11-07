namespace Malabak.SharedKernel.Contracts;

/// <summary>One line of a user's history from another module (a booking, a team membership, a report).</summary>
public sealed record UserActivityItem(string Kind, string Reference, string Status, DateTime OccurredAtUtc, string? Detail = null);

/// <summary>What one module knows about a user, for the admin user page (TASK-701): totals, counts per status and recent items.</summary>
public sealed record UserActivitySummary(string Source, int Total, IReadOnlyDictionary<string, int> Counts, IReadOnlyList<UserActivityItem> Recent);

/// <summary>
/// Implemented by modules that hold user-related history (bookings, teams). The admin user page asks every provider,
/// so Identity never reads other modules' tables.
/// </summary>
public interface IUserActivityProvider
{
    string Source { get; }

    Task<UserActivitySummary> GetAsync(Guid userId, int take, CancellationToken cancellationToken = default);
}
