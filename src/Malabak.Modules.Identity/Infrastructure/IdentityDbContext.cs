using Malabak.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Identity.Infrastructure;

public sealed class IdentityDbContext : DbContext
{
    public const string Schema = "identity";

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<PhoneChangeRequest> PhoneChangeRequests => Set<PhoneChangeRequest>();
    public DbSet<UserActionToken> UserActionTokens => Set<UserActionToken>();
    public DbSet<AdminRecoveryCode> AdminRecoveryCodes => Set<AdminRecoveryCode>();
    public DbSet<SmsDispatch> SmsDispatches => Set<SmsDispatch>();

    public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}
