using System.Globalization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Malabak.Api.Endpoints;
using Malabak.Api.Infrastructure;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Configuration;
using Malabak.Api.Infrastructure.Errors;
using Malabak.Api.Infrastructure.Health;
using Malabak.Api.Infrastructure.Hangfire;
using Malabak.Api.Infrastructure.Idempotency;
using Malabak.Api.Infrastructure.Localization;
using Malabak.Api.Infrastructure.Logging;
using Malabak.Api.Infrastructure.Media;
using Malabak.Api.Infrastructure.Persistence;
using Malabak.Api.Infrastructure.OpenApi;
using Malabak.Api.Infrastructure.OpenTelemetry;
using Malabak.Api.Infrastructure.Outbox;
using Malabak.Api.Infrastructure.Realtime;
using Malabak.Api.Infrastructure.Redis;
using Malabak.Api.Infrastructure.Security;
using Malabak.Api.Infrastructure.Seeding;
using Malabak.Api.Infrastructure.Validation;
using Malabak.Modules.Booking;
using Malabak.Modules.Booking.Application.Bookings.CreateBooking;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.Modules.Identity;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Matches;
using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Infrastructure;
using Malabak.Modules.Notifications;
using Malabak.Modules.Notifications.Infrastructure;
using Malabak.Modules.Teams;
using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Infrastructure;
using Malabak.Modules.Venues;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Time;
using Malabak.Modules.Audit;
using Malabak.Modules.Subscriptions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Configure configuration sources (User Secrets in Development, Environment Variables with MALABAK_ prefix)
builder.Configuration.ConfigureMalabakConfigurationSources(builder.Environment);

// Register strongly-typed Options with validation on start
builder.Services.AddMalabakOptions(builder.Configuration, builder.Environment);

// 1. Serilog Structured Logging with PII Masking
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.With<PiiMaskingEnricher>();
});

// 2. OpenTelemetry Traces & Metrics
builder.Services.AddMalabakOpenTelemetry();

// 3. ProblemDetails RFC 9457 & Global Exception Handler
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});

// 4. API Versioning
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
}).AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// 5. Swagger / OpenAPI Documentation
builder.Services.AddMalabakSwagger();

// 6. Redis Infrastructure & Health Checks
builder.Services.AddRedisInfrastructure(builder.Environment);

builder.Services.AddHealthChecks()
    .AddCheck("live", () => HealthCheckResult.Healthy("Liveness check succeeded"), tags: ["live"])
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

// 7. Hangfire Background Jobs
builder.Services.AddHangfireInfrastructure(builder.Configuration, builder.Environment);

// 8. Outbox & Domain Events Infrastructure
builder.Services.AddMalabakOutbox();

// 9. Reusable Idempotency Infrastructure
builder.Services.AddMalabakIdempotency();

// 10. Realtime & SignalR Infrastructure (WS Tickets & Hubs)
builder.Services.AddRealtimeInfrastructure(builder.Configuration);

// 11. Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPasswordAuthRateLimit();
    options.AddPolicy(MediaEndpoints.UploadRateLimitPolicy, http => RateLimitPartition.GetSlidingWindowLimiter(
        http.User.FindFirst("sub")?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new SlidingWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromHours(1), SegmentsPerWindow = 6, QueueLimit = 0 }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100,
                QueueLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));
});

// 8. Security Infrastructure (Headers, CORS, Cloudflare, Distributed Rate Limiter)
builder.Services.AddMalabakSecurity(builder.Configuration);

// 9. Localization (Arabic primary RTL, English)
builder.Services.AddLocalization();
var supportedCultures = new[] { new CultureInfo("ar-EG"), new CultureInfo("en-US") };
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture("ar-EG");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});

// 10. Security & Fallback Authorization Policy (Deny by default with ES256 JWT)
// Two schemes with the same signing keys but different audiences: a player/venue token is rejected on
// /admin endpoints and an admin token is rejected everywhere else (SECURITY.md §3.9).
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer()
    .AddJwtBearer(MalabakAuthorization.AdminScheme);

foreach (var (scheme, isAdmin) in new[] { (JwtBearerDefaults.AuthenticationScheme, false), (MalabakAuthorization.AdminScheme, true) })
{
    builder.Services.AddOptions<JwtBearerOptions>(scheme)
        .Configure<IJwtKeyProvider, IOptions<JwtOptions>>((options, keyProvider, jwtOptions) =>
        {
            var opts = jwtOptions.Value;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = keyProvider.GetValidationKeys(),
                ValidateIssuer = true,
                ValidIssuer = opts.Issuer,
                ValidateAudience = true,
                ValidAudience = isAdmin ? opts.AdminAudience : opts.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256]
            };

            // A revoked device session stops working immediately, not when its access token expires (SECURITY.md §3.5).
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var sessionId = context.Principal?.GetSessionId();
                    var store = context.HttpContext.RequestServices.GetRequiredService<ISessionStore>();
                    if (sessionId is null || !await store.IsActiveAsync(sessionId.Value, context.HttpContext.RequestAborted))
                    {
                        context.Fail("session_revoked");
                    }
                }
            };
        });
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IDeviceContext, HttpDeviceContext>();

builder.Services.AddMalabakAuthorization();

// 11. Database Contexts (Modular Monolith - Schema per module)
var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres");
builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseNpgsql(postgresConnectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema)));

builder.Services.AddDbContext<VenuesDbContext>(options =>
    options.UseNpgsql(postgresConnectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", VenuesDbContext.Schema)));

builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseNpgsql(postgresConnectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", BookingDbContext.Schema)));

builder.Services.AddDbContext<TeamsDbContext>(options =>
    options.UseNpgsql(postgresConnectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", TeamsDbContext.Schema)));
builder.Services.AddScoped<ITeamsDbContext>(sp => sp.GetRequiredService<TeamsDbContext>());

builder.Services.AddDbContext<MatchesDbContext>(options =>
    options.UseNpgsql(postgresConnectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MatchesDbContext.Schema)));
builder.Services.AddScoped<IMatchesDbContext>(sp => sp.GetRequiredService<MatchesDbContext>());

builder.Services.AddDbContext<MediaDbContext>(options =>
    options.UseNpgsql(postgresConnectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MediaDbContext.Schema)));
builder.Services.AddMediaStorage(builder.Configuration, builder.Environment);
builder.Services.AddScoped<MediaService>();
builder.Services.AddScoped<Malabak.Modules.Booking.Application.Abstractions.IProofFileStore, MediaProofFileStore>();
builder.Services.AddScoped<Malabak.SharedKernel.Contracts.IPersonalDataContributor, MediaPersonalData>();

builder.Services.AddDbContext<NotificationsDbContext>(options =>
    options.UseNpgsql(postgresConnectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", NotificationsDbContext.Schema)));

builder.Services.AddDbContext<Malabak.Modules.Audit.Infrastructure.AuditDbContext>(options =>
    options.UseNpgsql(postgresConnectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Malabak.Modules.Audit.Infrastructure.AuditDbContext.Schema)));
builder.Services.AddScoped<Malabak.SharedKernel.Contracts.IAuditRequestContext, Malabak.Api.Infrastructure.Audit.HttpAuditRequestContext>();
builder.Services.AddAuditModule();

builder.Services.AddDbContext<Malabak.Modules.Subscriptions.Infrastructure.SubscriptionsDbContext>(options =>
    options.UseNpgsql(postgresConnectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Malabak.Modules.Subscriptions.Infrastructure.SubscriptionsDbContext.Schema)));
builder.Services.AddSubscriptionsModule();

builder.Services.AddDbContext<Malabak.Api.Infrastructure.Platform.PlatformDbContext>(options =>
    options.UseNpgsql(postgresConnectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Malabak.Api.Infrastructure.Platform.PlatformDbContext.Schema)));
builder.Services.AddScoped<Malabak.Api.Infrastructure.Platform.PlatformService>();
builder.Services.AddScoped<Malabak.SharedKernel.Contracts.IPlatformSettings>(sp => sp.GetRequiredService<Malabak.Api.Infrastructure.Platform.PlatformService>());
builder.Services.AddScoped<Malabak.SharedKernel.Contracts.IFeatureFlags>(sp => sp.GetRequiredService<Malabak.Api.Infrastructure.Platform.PlatformService>());

// 12. Field-level encryption, blind indexes and the persisted Data Protection key ring
builder.Services.AddFieldEncryption(builder.Configuration, builder.Environment, postgresConnectionString);

// 13. Dependency Injection
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<ITimeZoneProvider, CairoTimeZoneProvider>();
builder.Services.AddSingleton<IErrorMessageLocalizer, ErrorMessageLocalizer>();
builder.Services.AddMalabakValidation();
builder.Services.AddMalabakDataSeeders();
builder.Services.AddScoped<IBookingConflictChecker, BookingConflictChecker>();
builder.Services.AddSingleton<Malabak.Modules.Venues.Application.Availability.IAvailabilityCacheStore, Malabak.Api.Infrastructure.Caching.RedisAvailabilityCacheStore>();
builder.Services.AddSingleton<Malabak.Modules.Venues.Application.Quotes.IQuoteTokenSigner, Malabak.Api.Infrastructure.Security.HmacQuoteTokenSigner>();
builder.Services.AddSingleton(new Malabak.Modules.Venues.Application.Home.HomeFeedSettings(builder.Configuration.GetValue<bool>("Features:SponsoredHomeItems")));
builder.Services.AddHostedService<Malabak.Api.Infrastructure.AccountAnonymizationService>();
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddVenuesModule();
builder.Services.AddBookingModule();
builder.Services.AddTeamsModule();
builder.Services.AddMatchesModule();
var fcmSection = builder.Configuration.GetSection("Push:Fcm");
builder.Services.AddNotificationsModule(string.IsNullOrWhiteSpace(fcmSection["PrivateKeyPem"])
    ? null
    : new FcmOptions(fcmSection["ProjectId"]!, fcmSection["ClientEmail"]!, fcmSection["PrivateKeyPem"]!));
builder.Services.AddHostedService<NotificationDispatchService>();

var app = builder.Build();

// Applies pending EF Core migrations for every module, then exits. Run as the deploy's release step
// (fly.toml release_command) so schema changes land once, before new instances start.
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await DatabaseMigrator.MigrateAllAsync(scope.ServiceProvider);
    return;
}

// Hosts without a release step or terminal (e.g. MonsterASP, Render free tier) migrate on startup instead; off by default.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await DatabaseMigrator.MigrateAllAsync(scope.ServiceProvider);
}

// CLI Data Seeding Commands
if (args.Contains("--seed-reference"))
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<ReferenceDataSeeder>();
    await seeder.SeedAsync();
    return;
}

if (args.Contains("--seed-demo"))
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
    await seeder.SeedAsync();
    return;
}

if (args.Contains("--seed-dev-accounts"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DevAccountsSeeder>().SeedAsync();
    return;
}

// Configure Middleware Pipeline
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.GetLevel = (httpContext, _, ex) =>
    {
        if (httpContext.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            return LogEventLevel.Verbose;
        }

        return ex != null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError
            ? LogEventLevel.Error
            : LogEventLevel.Information;
    };
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("QueryString", QueryStringRedactor.RedactQueryString(httpContext.Request.QueryString));
        diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
    };
});

app.UseExceptionHandler();
app.UseStatusCodePages();

var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("ar-EG")
    .AddSupportedCultures("ar-EG", "en-US")
    .AddSupportedUICultures("ar-EG", "en-US");
localizationOptions.RequestCultureProviders.Insert(0, new XLocaleRequestCultureProvider());
app.UseRequestLocalization(localizationOptions);

// Never in Production. Staging exposes it so the team can exercise the deployed API.
if (app.Environment.IsDevelopment() || app.Environment.IsStaging() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMalabakSecurity();
app.UseMiddleware<Malabak.Api.Infrastructure.Platform.MaintenanceModeMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMalabakIdempotency();

// Hangfire Dashboard (Secured Admin & Cloudflare Access)
app.UseHangfireInfrastructure();

// Health check endpoints
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = HealthCheckResponseWriter.WriteResponseAsync
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteResponseAsync
}).AllowAnonymous();

// Minimal API test endpoint
var apiVersionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .ReportApiVersions()
    .Build();

var v1 = app.MapGroup("/api/v{version:apiVersion}")
    .WithApiVersionSet(apiVersionSet);

v1.MapGet("/ping", () => Results.Ok(new { message = "Malabak API is operational", status = "Healthy" }))
    .MapToApiVersion(1, 0)
    .AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    v1.MapPost("/test/idempotent-action", (TestIdempotencyRequest request) =>
        Results.Created($"/api/v1/test/{request.Id}", new { status = "Created", id = request.Id, value = request.Value }))
        .MapToApiVersion(1, 0)
        .RequireIdempotency()
        .AllowAnonymous();

    v1.MapPost("/test/validation", (TestValidationRequest request) =>
        Result.Success(new { validated = true, name = request.Name }).ToHttpResult())
        .MapToApiVersion(1, 0)
        .WithValidation<TestValidationRequest>()
        .AllowAnonymous();

    v1.MapGet("/test/result-error/{code}", (string code, HttpContext httpContext) =>
    {
        var error = code switch
        {
            "not-found" => Error.NotFound("not_found", "Item not found"),
            "unauthorized" => Error.Unauthorized("unauthorized", "Access denied"),
            "forbidden" => Error.Forbidden("forbidden", "Forbidden action"),
            "conflict" => Error.Conflict("conflict", "Duplicate entity"),
            "validation" => Error.Validation("validation_failed", "Input validation failed"),
            _ => Error.Failure(code, "General domain failure")
        };
        return Result.Failure(error).ToHttpResult(httpContext: httpContext);
    })
    .MapToApiVersion(1, 0)
    .AllowAnonymous();

    v1.MapGet("/test/auth-fallback-protected", () => Results.Ok(new { secret = "data" }))
        .MapToApiVersion(1, 0);

    v1.MapGet("/test/rate-limited", () => Results.Ok(new { message = "rate limit test ok" }))
        .MapToApiVersion(1, 0)
        .RequireDistributedRateLimiting("test-policy", permitLimit: 3, windowSeconds: 10)
        .AllowAnonymous();

    v1.MapGet("/test/client-ip", (HttpContext httpContext) => Results.Ok(new
    {
        clientIp = httpContext.Connection.RemoteIpAddress?.ToString()
    }))
    .MapToApiVersion(1, 0)
    .AllowAnonymous();
}

app.MapAuthEndpoints(apiVersionSet);
app.MapPhoneChangeEndpoints(apiVersionSet);
app.MapVenueManagerEndpoints(apiVersionSet);
app.MapMeEndpoints(apiVersionSet);
app.MapPaymentMethodEndpoints(apiVersionSet);
app.MapVenueApprovalEndpoints(apiVersionSet);
app.MapBookingPolicyEndpoints(apiVersionSet);
app.MapPasswordAuthEndpoints(apiVersionSet);
app.MapAdminAuthEndpoints(apiVersionSet);
app.MapStepUpEndpoints(apiVersionSet);
app.MapStaffEndpoints(apiVersionSet);
app.MapNotificationEndpoints(apiVersionSet);
app.MapSmsWebhookEndpoints(apiVersionSet);
app.MapProfileEndpoints(apiVersionSet);
app.MapReferenceDataEndpoints(apiVersionSet);
app.MapMediaEndpoints(apiVersionSet);
app.MapSpecialScheduleEndpoints(apiVersionSet);
app.MapPriceRuleEndpoints(apiVersionSet);
app.MapPublicVenueEndpoints(apiVersionSet);
app.MapBookingQuoteEndpoints(apiVersionSet);
app.MapVenueCustomerEndpoints(apiVersionSet);
app.MapVenueReportEndpoints(apiVersionSet);
app.MapAdminAuditEndpoints(apiVersionSet);
app.MapPlatformEndpoints(apiVersionSet);
app.MapSubscriptionEndpoints(apiVersionSet);
app.MapPlayerVenueListEndpoints(apiVersionSet);
app.MapPrivacyEndpoints(apiVersionSet);
app.MapRealtimeEndpoints(apiVersionSet);
app.MapBookingEndpoints(apiVersionSet);
app.MapTeamEndpoints(apiVersionSet);
app.MapChallengeEndpoints(apiVersionSet);
app.MapMatchEndpoints(apiVersionSet);

// WebSockets only (SECURITY.md): long polling and server-sent events would validate the single-use ticket on every request.
app.MapHub<BookingHub>("/hubs/booking", options => options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets);

await app.RunAsync();

// Required for WebApplicationFactory in integration tests
public partial class Program
{
    protected Program() { }
}
