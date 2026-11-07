namespace Malabak.SharedKernel.Authorization;

/// <summary>
/// Fine-grained permission catalog. Each value is also the name of an authorization policy.
/// Platform permissions come from a user's roles; venue-scoped permissions are granted per venue
/// (owner today, staff memberships with TASK-212) and are checked against the specific venue.
/// </summary>
public static class Permissions
{
    // Player
    public const string ProfileManage = "profile.manage";
    public const string BookingsCreate = "bookings.create";
    public const string TeamsParticipate = "teams.participate";

    // Venue management — entry permission for the venue-manager API surface
    public const string VenuesManage = "venues.manage";

    // Venue-scoped (always evaluated against a specific venue)
    public const string VenueView = "venue.view";
    public const string VenueEdit = "venue.edit";
    public const string VenueDelete = "venue.delete";
    public const string VenuePublish = "venue.publish";
    public const string PitchesManage = "venue.pitches.manage";
    public const string SchedulesManage = "venue.schedules.manage";
    public const string BlockedSlotsManage = "venue.blocked_slots.manage";
    public const string PoliciesManage = "venue.policies.manage";
    public const string PaymentMethodsManage = "venue.payment_methods.manage";
    public const string BookingsView = "venue.bookings.view";
    public const string BookingsApprove = "venue.bookings.approve";
    public const string PaymentProofsReview = "venue.payment_proofs.review";
    public const string CustomersView = "venue.customers.view";
    public const string ReportsView = "venue.reports.view";
    public const string StaffManage = "venue.staff.manage";

    // Platform administration
    public const string AdminUsersView = "admin.users.view";
    public const string AdminUsersManage = "admin.users.manage";
    public const string AdminAccountsManage = "admin.accounts.manage";
    public const string AdminImpersonate = "admin.impersonate";
    public const string AdminVenuesApprove = "admin.venues.approve";
    public const string AdminBookingsView = "admin.bookings.view";
    public const string AdminDisputesResolve = "admin.disputes.resolve";
    public const string AdminSettingsManage = "admin.settings.manage";
    public const string AuditLogView = "admin.audit_log.view";
    public const string SupportTicketsManage = "support.tickets.manage";
    public const string SubscriptionsManage = "finance.subscriptions.manage";
    public const string InvoicesView = "finance.invoices.view";

    public static readonly IReadOnlyCollection<string> VenueScoped =
    [
        VenueView,
        VenueEdit,
        VenueDelete,
        VenuePublish,
        PitchesManage,
        SchedulesManage,
        BlockedSlotsManage,
        PoliciesManage,
        PaymentMethodsManage,
        BookingsView,
        BookingsApprove,
        PaymentProofsReview,
        CustomersView,
        ReportsView,
        StaffManage
    ];

    public static readonly IReadOnlyCollection<string> Platform =
    [
        ProfileManage,
        BookingsCreate,
        TeamsParticipate,
        VenuesManage,
        AdminUsersView,
        AdminUsersManage,
        AdminAccountsManage,
        AdminImpersonate,
        AdminVenuesApprove,
        AdminBookingsView,
        AdminDisputesResolve,
        AdminSettingsManage,
        AuditLogView,
        SupportTicketsManage,
        SubscriptionsManage,
        InvoicesView
    ];
}
