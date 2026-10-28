using Microsoft.EntityFrameworkCore;

namespace Malabak.Api.Infrastructure.Security;

public sealed class SecurityDbContext : DbContext
{
    public const string Schema = "security";

    public DbSet<DataEncryptionKey> DataEncryptionKeys => Set<DataEncryptionKey>();
    public DbSet<DataProtectionKeyRecord> DataProtectionKeys => Set<DataProtectionKeyRecord>();

    public SecurityDbContext(DbContextOptions<SecurityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<DataEncryptionKey>(builder =>
        {
            builder.ToTable("data_encryption_keys");
            builder.HasKey(k => k.Id);
            builder.Property(k => k.Id).UseIdentityAlwaysColumn();
            builder.Property(k => k.WrappedKey).IsRequired();
            builder.Property(k => k.KeyEncryptionKeyId).HasMaxLength(100).IsRequired();
            builder.Property(k => k.CreatedAtUtc).IsRequired();
            builder.Property(k => k.RowVersion).IsRowVersion();

            // At most one active key; a second node racing to create one hits this and reloads instead.
            builder.HasIndex(k => k.IsActive)
                .IsUnique()
                .HasFilter("\"IsActive\" = TRUE")
                .HasDatabaseName("ux_data_encryption_keys_single_active");
        });

        modelBuilder.Entity<DataProtectionKeyRecord>(builder =>
        {
            builder.ToTable("data_protection_keys");
            builder.HasKey(k => k.Id);
            builder.Property(k => k.FriendlyName).HasMaxLength(200).IsRequired();
            builder.Property(k => k.Xml).IsRequired();
            builder.Property(k => k.CreatedAtUtc).IsRequired();
        });
    }
}
