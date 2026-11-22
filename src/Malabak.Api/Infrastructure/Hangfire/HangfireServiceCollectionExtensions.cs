using Hangfire;
using Hangfire.PostgreSql;
using Malabak.Api.Infrastructure.Configuration;
using Malabak.Modules.Booking.Infrastructure.Jobs;
using Microsoft.Extensions.Options;

namespace Malabak.Api.Infrastructure.Hangfire;

public static class HangfireServiceCollectionExtensions
{
    public static IServiceCollection AddHangfireInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var options = configuration.GetSection(HangfireOptions.SectionName).Get<HangfireOptions>() ?? new HangfireOptions();
        if (!options.Enabled)
        {
            return services;
        }

        var connectionString = configuration.GetSection(HangfireOptions.SectionName)["ConnectionString"]
            ?? configuration.GetConnectionString("Postgres");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("PostgreSQL connection string for Hangfire could not be resolved.");
        }

        services.AddHangfire((sp, config) =>
        {
            var options = sp.GetRequiredService<IOptions<HangfireOptions>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var retryLogger = loggerFactory.CreateLogger<HangfireRetryPolicyFilterAttribute>();

            config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString), new PostgreSqlStorageOptions
                {
                    SchemaName = options.SchemaName,
                    PrepareSchemaIfNecessary = true,
                    QueuePollInterval = TimeSpan.FromSeconds(options.PollIntervalSeconds),
                    InvisibilityTimeout = TimeSpan.FromMinutes(30)
                })
                .UseFilter(new HangfireMetricsFilterAttribute())
                .UseFilter(new HangfireRetryPolicyFilterAttribute(options.DefaultRetryAttempts, retryLogger));
        });

        services.AddHangfireServer((sp, serverOptions) =>
        {
            var options = sp.GetRequiredService<IOptions<HangfireOptions>>().Value;
            serverOptions.WorkerCount = options.WorkerCount;
            serverOptions.Queues = options.Queues;
            serverOptions.ShutdownTimeout = TimeSpan.FromSeconds(15);
        });

        return services;
    }

    public static WebApplication UseHangfireInfrastructure(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.Services.GetRequiredService<IOptions<HangfireOptions>>().Value;

        if (!options.Enabled)
        {
            return app;
        }

        if (options.EnableDashboard)
        {
            var authFilter = new HangfireDashboardAuthorizationFilter(
                app.Services.GetRequiredService<IOptions<HangfireOptions>>(),
                app.Environment,
                app.Services.GetRequiredService<ILogger<HangfireDashboardAuthorizationFilter>>());

            app.UseHangfireDashboard(options.DashboardPath, new DashboardOptions
            {
                Authorization = [authFilter],
                DashboardTitle = "Malabak Job Scheduler",
                DisplayStorageConnectionString = false
            });
        }

        using var scope = app.Services.CreateScope();
        var recurringJobs = scope.ServiceProvider.GetService<IRecurringJobManager>();
        if (recurringJobs != null)
        {
            recurringJobs.AddOrUpdate<ExpireBookingHoldsJob>(
                ExpireBookingHoldsJob.RecurringJobId,
                job => job.ExecuteRecurringSweepAsync(CancellationToken.None),
                ExpireBookingHoldsJob.CronSchedule);

            recurringJobs.AddOrUpdate<CompleteBookingsJob>(
                CompleteBookingsJob.RecurringJobId,
                job => job.ExecuteRecurringSweepAsync(CancellationToken.None),
                CompleteBookingsJob.CronSchedule);

            recurringJobs.AddOrUpdate<BookingRemindersJob>(
                BookingRemindersJob.RecurringJobId,
                job => job.ExecuteRecurringSweepAsync(CancellationToken.None),
                BookingRemindersJob.CronSchedule);

            recurringJobs.AddOrUpdate<Malabak.Api.Infrastructure.Platform.TrashPurgeJob>(
                Malabak.Api.Infrastructure.Platform.TrashPurgeJob.RecurringJobId,
                job => job.ExecuteAsync(CancellationToken.None),
                Malabak.Api.Infrastructure.Platform.TrashPurgeJob.CronSchedule);

            recurringJobs.AddOrUpdate<Malabak.Api.Infrastructure.Platform.DataRetentionJob>(
                Malabak.Api.Infrastructure.Platform.DataRetentionJob.RecurringJobId,
                job => job.ExecuteAsync(CancellationToken.None),
                Malabak.Api.Infrastructure.Platform.DataRetentionJob.CronSchedule);

            recurringJobs.AddOrUpdate<Malabak.Api.Infrastructure.Idempotency.IdempotencyCleanupJob>(
                "platform-idempotency-cleanup",
                job => job.ExecuteAsync(CancellationToken.None),
                "45 3 * * *");

            recurringJobs.AddOrUpdate<ProofRetentionJob>(
                ProofRetentionJob.RecurringJobId,
                job => job.ExecuteRecurringCleanupAsync(CancellationToken.None),
                ProofRetentionJob.CronSchedule);

            recurringJobs.AddOrUpdate<Malabak.Modules.Matches.Infrastructure.Jobs.RankingRecalculationJob>(
                Malabak.Modules.Matches.Infrastructure.Jobs.RankingRecalculationJob.RecurringJobId,
                job => job.ExecuteRecalculationAsync(CancellationToken.None),
                Malabak.Modules.Matches.Infrastructure.Jobs.RankingRecalculationJob.CronSchedule);

            recurringJobs.AddOrUpdate<Malabak.Modules.Matches.Infrastructure.Jobs.MatchResultTimeoutJob>(
                Malabak.Modules.Matches.Infrastructure.Jobs.MatchResultTimeoutJob.RecurringJobId,
                job => job.ExecuteSweepAsync(CancellationToken.None),
                Malabak.Modules.Matches.Infrastructure.Jobs.MatchResultTimeoutJob.CronSchedule);

            // Subscriptions are retired: drop the renewal/expiry job left in storage by earlier deploys.
            recurringJobs.RemoveIfExists(Malabak.Modules.Subscriptions.Infrastructure.SubscriptionLifecycleJob.RecurringJobId);
        }

        return app;
    }
}
