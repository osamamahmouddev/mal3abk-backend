using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;

namespace Malabak.Modules.Venues.Domain;

/// <summary>Per-venue staff roles; each maps to a fixed, least-privilege set of venue-scoped permissions.</summary>
public enum VenueStaffRole
{
    Manager = 0,
    Receptionist = 1,
    Accountant = 2
}

public static class VenueStaffRolePermissions
{
    private static readonly IReadOnlyDictionary<VenueStaffRole, IReadOnlySet<string>> _map = new Dictionary<VenueStaffRole, IReadOnlySet<string>>
    {
        // Staff management, venue deletion/publishing and payout accounts stay with the owner.
        [VenueStaffRole.Manager] = new HashSet<string>(StringComparer.Ordinal)
        {
            Permissions.VenueView, Permissions.VenueEdit, Permissions.PitchesManage, Permissions.SchedulesManage,
            Permissions.BlockedSlotsManage, Permissions.PoliciesManage, Permissions.BookingsView, Permissions.BookingsApprove,
            Permissions.PaymentProofsReview, Permissions.CustomersView, Permissions.ReportsView
        },
        [VenueStaffRole.Receptionist] = new HashSet<string>(StringComparer.Ordinal)
        {
            Permissions.VenueView, Permissions.BlockedSlotsManage, Permissions.BookingsView, Permissions.BookingsApprove, Permissions.CustomersView
        },
        [VenueStaffRole.Accountant] = new HashSet<string>(StringComparer.Ordinal)
        {
            Permissions.VenueView, Permissions.BookingsView, Permissions.PaymentProofsReview, Permissions.ReportsView
        }
    };

    public static IReadOnlySet<string> For(VenueStaffRole role) => _map[role];
}

public sealed class VenueStaffMember : Entity<Guid>
{
    private VenueStaffMember()
    {
    }

    public Guid VenueId { get; private set; }
    public Guid UserId { get; private set; }
    public VenueStaffRole Role { get; private set; }
    public Guid AddedByUserId { get; private set; }

    public static VenueStaffMember Create(Guid venueId, Guid userId, VenueStaffRole role, Guid addedBy, DateTime nowUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        VenueId = venueId,
        UserId = userId,
        Role = role,
        AddedByUserId = addedBy,
        CreatedAtUtc = nowUtc
    };

    public void ChangeRole(VenueStaffRole role, DateTime nowUtc)
    {
        Role = role;
        UpdatedAtUtc = nowUtc;
    }

    public void Remove(DateTime nowUtc)
    {
        IsDeleted = true;
        UpdatedAtUtc = nowUtc;
    }
}

public enum StaffInvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Revoked = 2
}

/// <summary>Invitation bound to an email or phone; the link token is single-use, 48h, and stored only as a hash.</summary>
public sealed class VenueStaffInvitation : Entity<Guid>
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(48);

    private VenueStaffInvitation()
    {
    }

    public Guid VenueId { get; private set; }
    public string? Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public VenueStaffRole Role { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }
    public StaffInvitationStatus Status { get; private set; }
    public Guid InvitedByUserId { get; private set; }
    public Guid? AcceptedByUserId { get; private set; }
    public DateTime? AcceptedAtUtc { get; private set; }

    public static VenueStaffInvitation Create(
        Guid venueId, string? email, string? normalizedPhone, VenueStaffRole role, string tokenHash, Guid invitedBy, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(email) == string.IsNullOrWhiteSpace(normalizedPhone))
        {
            throw new ArgumentException("Invite by exactly one of email or phone.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        return new VenueStaffInvitation
        {
            Id = Guid.CreateVersion7(),
            VenueId = venueId,
            Email = email?.Trim().ToLowerInvariant(),
            PhoneNumber = normalizedPhone,
            Role = role,
            TokenHash = tokenHash,
            ExpiresAtUtc = nowUtc + Lifetime,
            Status = StaffInvitationStatus.Pending,
            InvitedByUserId = invitedBy,
            CreatedAtUtc = nowUtc
        };
    }

    public bool IsUsable(DateTime nowUtc) => Status == StaffInvitationStatus.Pending && ExpiresAtUtc > nowUtc;

    /// <summary>Resend issues a new token (the old link stops working) and restarts the 48h window.</summary>
    public void Reissue(string tokenHash, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        TokenHash = tokenHash;
        ExpiresAtUtc = nowUtc + Lifetime;
        UpdatedAtUtc = nowUtc;
    }

    public void Revoke(DateTime nowUtc)
    {
        Status = StaffInvitationStatus.Revoked;
        UpdatedAtUtc = nowUtc;
    }

    public void Accept(Guid userId, DateTime nowUtc)
    {
        Status = StaffInvitationStatus.Accepted;
        AcceptedByUserId = userId;
        AcceptedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }
}
