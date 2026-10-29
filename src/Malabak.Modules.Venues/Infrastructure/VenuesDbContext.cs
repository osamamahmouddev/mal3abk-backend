using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Infrastructure;

public sealed class VenuesDbContext : BaseDbContext
{
    public const string Schema = "venues";
    public override string ModuleSchema => Schema;

    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Pitch> Pitches => Set<Pitch>();
    public DbSet<OperatingSchedule> OperatingSchedules => Set<OperatingSchedule>();
    public DbSet<PriceRule> PriceRules => Set<PriceRule>();
    public DbSet<BlockedSlot> BlockedSlots => Set<BlockedSlot>();
    public DbSet<VenuePolicy> Policies => Set<VenuePolicy>();
    public DbSet<VenuePaymentMethod> PaymentMethods => Set<VenuePaymentMethod>();

    public VenuesDbContext(DbContextOptions<VenuesDbContext> options, IClock? clock = null)
        : base(options, clock)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VenuesDbContext).Assembly);
    }
}
