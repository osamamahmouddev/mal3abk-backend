using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Infrastructure;

public sealed class VenuesDbContext : DbContext
{
    public const string Schema = "venues";

    private readonly IVenueTenantContext? _tenant;

    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Pitch> Pitches => Set<Pitch>();
    public DbSet<OperatingSchedule> OperatingSchedules => Set<OperatingSchedule>();
    public DbSet<PriceRule> PriceRules => Set<PriceRule>();
    public DbSet<BlockedSlot> BlockedSlots => Set<BlockedSlot>();
    public DbSet<VenuePolicy> Policies => Set<VenuePolicy>();
    public DbSet<VenuePaymentMethod> PaymentMethods => Set<VenuePaymentMethod>();
    public DbSet<VenueApprovalEvent> ApprovalEvents => Set<VenueApprovalEvent>();
    public DbSet<VenueStaffMember> StaffMembers => Set<VenueStaffMember>();
    public DbSet<VenueStaffInvitation> StaffInvitations => Set<VenueStaffInvitation>();
    public DbSet<ScheduleOverride> ScheduleOverrides => Set<ScheduleOverride>();
    public DbSet<PitchWeeklyHours> PitchWeeklyHours => Set<PitchWeeklyHours>();
    public DbSet<BookingRules> BookingRules => Set<BookingRules>();

    public VenuesDbContext(DbContextOptions<VenuesDbContext> options)
        : base(options)
    {
    }

    public VenuesDbContext(DbContextOptions<VenuesDbContext> options, IVenueTenantContext tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    // Read by the query filters at execution time, so each request sees its own tenant scope.
    private bool IsTenantRestricted => _tenant?.IsRestricted ?? false;
    private IReadOnlyCollection<Guid> TenantVenueIds => _tenant?.VenueIds ?? [];

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VenuesDbContext).Assembly);

        modelBuilder.Entity<Venue>().HasQueryFilter(v => !IsTenantRestricted || TenantVenueIds.Contains(v.Id));
        modelBuilder.Entity<Pitch>().HasQueryFilter(e => !IsTenantRestricted || TenantVenueIds.Contains(e.VenueId));
        modelBuilder.Entity<OperatingSchedule>().HasQueryFilter(e => !IsTenantRestricted || TenantVenueIds.Contains(e.VenueId));
        modelBuilder.Entity<PriceRule>().HasQueryFilter(e => !IsTenantRestricted || TenantVenueIds.Contains(e.VenueId));
        modelBuilder.Entity<BlockedSlot>().HasQueryFilter(e => !IsTenantRestricted || TenantVenueIds.Contains(e.VenueId));
        modelBuilder.Entity<VenuePolicy>().HasQueryFilter(e => !IsTenantRestricted || TenantVenueIds.Contains(e.VenueId));
        modelBuilder.Entity<VenuePaymentMethod>().HasQueryFilter(e => !IsTenantRestricted || TenantVenueIds.Contains(e.VenueId));
        modelBuilder.Entity<VenueApprovalEvent>().HasQueryFilter(e => !IsTenantRestricted || TenantVenueIds.Contains(e.VenueId));
        modelBuilder.Entity<VenueStaffMember>().HasQueryFilter(e => !IsTenantRestricted || TenantVenueIds.Contains(e.VenueId));
        modelBuilder.Entity<VenueStaffInvitation>().HasQueryFilter(e => !IsTenantRestricted || TenantVenueIds.Contains(e.VenueId));
        modelBuilder.Entity<ScheduleOverride>().HasQueryFilter(e => !IsTenantRestricted || TenantVenueIds.Contains(e.VenueId));
        modelBuilder.Entity<PitchWeeklyHours>().HasQueryFilter(e => !IsTenantRestricted || TenantVenueIds.Contains(e.VenueId));
        modelBuilder.Entity<BookingRules>().HasQueryFilter(e => !IsTenantRestricted || TenantVenueIds.Contains(e.VenueId));
    }
}
