using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Malabak.SharedKernel.Persistence;

public abstract class BaseDbContext : DbContext, IUnitOfWork
{
    private readonly IClock? _clock;

    public abstract string ModuleSchema { get; }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    private readonly HashSet<object> _hardDeletes = new(ReferenceEqualityComparer.Instance);

    protected BaseDbContext(DbContextOptions options, IClock? clock = null)
        : base(options)
    {
        _clock = clock;
    }

    /// <summary>
    /// Marks entities for physical deletion on the next save, bypassing the soft-delete conversion. For data that
    /// must really be erased, such as personal data removed when an account is anonymized.
    /// </summary>
    public void HardDelete<TEntity>(IEnumerable<TEntity> entities)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entities);

        foreach (var entity in entities)
        {
            _hardDeletes.Add(entity);
            Remove(entity);
        }
    }

    internal bool IsMarkedForHardDelete(object entity) => _hardDeletes.Contains(entity);

    internal void ClearHardDeletes() => _hardDeletes.Clear();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        if (_clock is not null)
        {
            optionsBuilder.AddInterceptors(
                new AuditableEntityInterceptor(_clock),
                new SoftDeleteInterceptor(_clock),
                new OutboxInterceptor(_clock));
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        if (!string.IsNullOrWhiteSpace(ModuleSchema))
        {
            modelBuilder.HasDefaultSchema(ModuleSchema);
        }

        ConfigureConventions(modelBuilder);
        ConfigureOutboxAndInbox(modelBuilder);
    }

    private static void ConfigureOutboxAndInbox(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages");
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Type).HasMaxLength(500).IsRequired();
            builder.Property(m => m.Content).HasColumnType("text").IsRequired();
            builder.Property(m => m.Error).HasColumnType("text");
            builder.Property(m => m.LockToken).HasMaxLength(128);
            builder.HasIndex(m => new { m.ProcessedOnUtc, m.NextRetryUtc });
        });

        modelBuilder.Entity<InboxMessage>(builder =>
        {
            builder.ToTable("inbox_messages");
            builder.HasKey(m => new { m.EventId, m.HandlerName });
            builder.Property(m => m.HandlerName).HasMaxLength(256).IsRequired();
            builder.Property(m => m.Error).HasColumnType("text");
        });

        modelBuilder.Entity<IdempotencyRecord>(builder =>
        {
            builder.ToTable("idempotency_records");
            builder.HasKey(r => r.Id);
            builder.Property(r => r.Key).HasMaxLength(256).IsRequired();
            builder.Property(r => r.RequestHash).HasMaxLength(64).IsRequired();
            builder.Property(r => r.HttpMethod).HasMaxLength(16).IsRequired();
            builder.Property(r => r.RequestPath).HasMaxLength(512).IsRequired();
            builder.Property(r => r.ResponseBody).HasColumnType("text");
            builder.Property(r => r.ResponseContentType).HasMaxLength(128);
            builder.HasIndex(r => r.Key).IsUnique();
            builder.HasIndex(r => r.ExpiresAtUtc);
        });
    }

    private static void ConfigureConventions(ModelBuilder modelBuilder)
    {
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => NormalizeUtc(v),
            v => NormalizeUtc(v));

        var nullableUtcConverter = new ValueConverter<DateTime?, DateTime?>(
            v => NormalizeNullableUtc(v),
            v => NormalizeNullableUtc(v));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            // 1. Soft Delete Global Query Filter
            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                var parameter = Expression.Parameter(clrType, "e");
                var property = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
                var notExpression = Expression.Not(property);
                var lambda = Expression.Lambda(notExpression, parameter);

                modelBuilder.Entity(clrType).HasQueryFilter(lambda);
            }

            // 2. Concurrency Token (PostgreSQL xmin)
            if (typeof(IVersionedEntity).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType)
                    .Property(nameof(IVersionedEntity.RowVersion))
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsConcurrencyToken();
            }

            // 3. UTC Timestamp Conventions
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(utcConverter);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(nullableUtcConverter);
                }
            }
        }
    }

    private static DateTime NormalizeUtc(DateTime v)
    {
        return v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc);
    }

    private static DateTime? NormalizeNullableUtc(DateTime? v)
    {
        if (!v.HasValue)
        {
            return null;
        }

        return NormalizeUtc(v.Value);
    }
}
