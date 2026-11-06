using Malabak.Modules.Venues.Application.Availability;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Venues.Infrastructure;

public sealed class VenuesDbContext : BaseDbContext
{
    public const string Schema = "venues";
    public override string ModuleSchema => Schema;

    private readonly IVenueTenantContext? _tenant;
    private readonly IAvailabilityCacheStore? _availabilityCache;
    private readonly ILogger<VenuesDbContext>? _logger;

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

    // Player-private lists: no tenant filter because no venue-manager endpoint ever reads them.
    public DbSet<FavoriteVenue> FavoriteVenues => Set<FavoriteVenue>();
    public DbSet<RecentlyViewedVenue> RecentlyViewedVenues => Set<RecentlyViewedVenue>();
    public DbSet<FeaturedPlacement> FeaturedPlacements => Set<FeaturedPlacement>();

    public VenuesDbContext(
        DbContextOptions<VenuesDbContext> options,
        IVenueTenantContext? tenant = null,
        IClock? clock = null,
        IAvailabilityCacheStore? availabilityCache = null,
        ILogger<VenuesDbContext>? logger = null)
        : base(options, clock)
    {
        _tenant = tenant;
        _availabilityCache = availabilityCache;
        _logger = logger;
    }

    /// <summary>
    /// Saves, then invalidates cached availability (TASK-301) for every venue whose hours, blocks, prices, rules, pitches
    /// or publication changed. Done here rather than in each handler so no write path can forget it.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var affectedVenues = AvailabilityAffectedVenues();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (_availabilityCache is not null)
        {
            foreach (var venueId in affectedVenues)
            {
                await InvalidateAsync(venueId, cancellationToken);
            }
        }

        return result;
    }

    private HashSet<Guid> AvailabilityAffectedVenues() => ChangeTracker.Entries()
        .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
        .Select(AvailabilityVenueId)
        .OfType<Guid>()
        .ToHashSet();

    private static Guid? AvailabilityVenueId(EntityEntry entry) => entry.Entity switch
    {
        Venue v => v.Id,
        Pitch p => p.VenueId,
        OperatingSchedule s => s.VenueId,
        PriceRule r => r.VenueId,
        BlockedSlot b => b.VenueId,
        ScheduleOverride o => o.VenueId,
        PitchWeeklyHours h => h.VenueId,
        BookingRules br => br.VenueId,
        _ => null
    };

    private async Task InvalidateAsync(Guid venueId, CancellationToken cancellationToken)
    {
        try
        {
            await _availabilityCache!.IncrementVersionAsync(venueId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The cache TTL bounds staleness; the committed change must not fail because the cache is down.
            _logger?.LogWarning(ex, "Availability cache invalidation failed for venue {VenueId}", venueId);
        }
    }

    // Read by the query filters at execution time, so each request sees its own tenant scope.
    private bool IsTenantRestricted => _tenant?.IsRestricted ?? false;
    private IReadOnlyCollection<Guid> TenantVenueIds => _tenant?.VenueIds ?? [];

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
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
