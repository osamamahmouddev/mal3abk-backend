using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Malabak.SharedKernel.Persistence;

public abstract class BaseDbContext : DbContext, IUnitOfWork
{
    private readonly IClock? _clock;

    public abstract string ModuleSchema { get; }

    protected BaseDbContext(DbContextOptions options, IClock? clock = null)
        : base(options)
    {
        _clock = clock;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        if (_clock is not null)
        {
            optionsBuilder.AddInterceptors(
                new AuditableEntityInterceptor(_clock),
                new SoftDeleteInterceptor(_clock));
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
