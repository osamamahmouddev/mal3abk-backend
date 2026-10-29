using Malabak.Modules.Identity.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Identity.Infrastructure;

public sealed class IdentityDbContext : BaseDbContext
{
    public const string Schema = "identity";
    public override string ModuleSchema => Schema;

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<PhoneChangeRequest> PhoneChangeRequests => Set<PhoneChangeRequest>();

    public IdentityDbContext(DbContextOptions<IdentityDbContext> options, IClock? clock = null)
        : base(options, clock)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}
