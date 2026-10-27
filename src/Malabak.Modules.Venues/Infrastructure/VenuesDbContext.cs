using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Infrastructure;

public sealed class VenuesDbContext : DbContext
{
    public const string Schema = "venues";

    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Pitch> Pitches => Set<Pitch>();
    public DbSet<OperatingSchedule> OperatingSchedules => Set<OperatingSchedule>();
    public DbSet<PriceRule> PriceRules => Set<PriceRule>();
    public DbSet<BlockedSlot> BlockedSlots => Set<BlockedSlot>();
    public DbSet<VenuePolicy> Policies => Set<VenuePolicy>();
    public DbSet<VenuePaymentMethod> PaymentMethods => Set<VenuePaymentMethod>();

    public VenuesDbContext(DbContextOptions<VenuesDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VenuesDbContext).Assembly);
    }
}
