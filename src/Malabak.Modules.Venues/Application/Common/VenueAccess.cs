using Malabak.SharedKernel.Authorization;

namespace Malabak.Modules.Venues.Application.Common;

public enum VenueAccessLevel
{
    None = 0,
    Staff = 1,
    Owner = 2
}

/// <summary>What a user may do on one specific venue.</summary>
public sealed record VenueAccess(Guid VenueId, VenueAccessLevel Level, IReadOnlySet<string> Permissions)
{
    public static VenueAccess None(Guid venueId) => new(venueId, VenueAccessLevel.None, new HashSet<string>());

    public static VenueAccess Owner(Guid venueId) =>
        new(venueId, VenueAccessLevel.Owner, new HashSet<string>(SharedKernel.Authorization.Permissions.VenueScoped, StringComparer.Ordinal));

    public static VenueAccess Staff(Guid venueId, Domain.VenueStaffRole role) =>
        new(venueId, VenueAccessLevel.Staff, Domain.VenueStaffRolePermissions.For(role));

    public bool HasAccess => Level != VenueAccessLevel.None;

    public bool Can(string permission) => Permissions.Contains(permission);
}
