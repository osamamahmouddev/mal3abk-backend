namespace Malabak.SharedKernel.Contracts;

/// <summary>What the trash shows for an item a module moved there.</summary>
public sealed record TrashedItem(string Title, string? Subtitle);

/// <summary>
/// One kind of thing the platform team can delete from the admin console (users, venues, teams, CRM contacts). The item
/// stays restorable in the trash for <see cref="TrashRules.Retention"/>; purging it is irreversible and erases what
/// identifies it while keeping the minimum accounting needs (bookings and payments keep pointing at the row), per SECURITY.md.
/// </summary>
public interface ITrashProvider
{
    /// <summary>The kind key used in URLs and the trash list: "user", "venue", "team" or "crm".</summary>
    string Kind { get; }

    /// <summary>The name of an item not deleted yet (to check the confirmation an admin typed), or null when there is none.</summary>
    Task<string?> GetNameAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<TrashedItem>> MoveToTrashAsync(Guid id, Guid adminId, CancellationToken cancellationToken = default);

    Task<Result> RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> PurgeAsync(Guid id, CancellationToken cancellationToken = default);
}

public static class TrashRules
{
    /// <summary>How long a deleted item can be restored before it is purged automatically.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);
}

/// <summary>Booking-module contract: active bookings still ahead, so nothing in use is deleted from under a player or a venue.</summary>
public interface IActiveBookingsCounter
{
    Task<int> CountForVenueAsync(Guid venueId, CancellationToken cancellationToken = default);

    Task<int> CountForPlayerAsync(Guid playerId, CancellationToken cancellationToken = default);
}

public static class TrashErrors
{
    public static readonly Error NotFound = Error.NotFound("Trash.NotFound", "The item was not found.");
    public static readonly Error UnknownKind = Error.NotFound("Trash.UnknownKind", "Unknown kind of item.");
    public static readonly Error StaffAccount = Error.Validation("Trash.StaffAccount", "Team accounts are disabled from the team accounts page, not deleted.");
    public static readonly Error OwnsVenues = Error.Conflict("Trash.OwnsVenues", "This user owns venues: delete them first.");
    public static readonly Error CannotRestore = Error.Conflict("Trash.CannotRestore", "The item can no longer be restored.");
    public static readonly Error ConfirmationMismatch = Error.Validation("Trash.ConfirmationMismatch", "Type the item's name exactly to confirm.");
    public static readonly Error ReasonRequired = Error.Validation("Trash.ReasonRequired", "Write why the item is deleted (5 to 500 characters).");

    public static Error HasUpcomingBookings(int count) =>
        Error.Conflict("Trash.HasUpcomingBookings", $"There are {count} upcoming active bookings: cancel them first.");
}
