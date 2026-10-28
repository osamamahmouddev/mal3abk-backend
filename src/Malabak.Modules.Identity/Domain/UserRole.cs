namespace Malabak.Modules.Identity.Domain;

public static class UserRole
{
    public const string Player = "Player";
    public const string VenueOwner = "VenueOwner";
    public const string VenueStaff = "VenueStaff";
    public const string Admin = "Admin";
    public const string SupportAgent = "SupportAgent";
    public const string Finance = "Finance";

    public static readonly IReadOnlyCollection<string> All =
    [
        Player,
        VenueOwner,
        VenueStaff,
        Admin,
        SupportAgent,
        Finance
    ];
}
