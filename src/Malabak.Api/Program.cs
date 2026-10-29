using System.Globalization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Malabak.Api.Endpoints;
using Malabak.Api.Infrastructure.Configuration;
using Malabak.Api.Infrastructure.Errors;
using Malabak.Api.Infrastructure.Health;
using Malabak.Api.Infrastructure.Hangfire;
using Malabak.Api.Infrastructure.Idempotency;
using Malabak.Api.Infrastructure.Localization;
using Malabak.Api.Infrastructure.Logging;
using Malabak.Api.Infrastructure.OpenApi;
using Malabak.Api.Infrastructure.OpenTelemetry;
using Malabak.Api.Infrastructure.Outbox;
using Malabak.Api.Infrastructure.Redis;
using Malabak.Api.Infrastructure.Security;
using Malabak.Api.Infrastructure.Seeding;
using Malabak.Api.Infrastructure.Validation;
using Malabak.Modules.Booking.Application.Bookings.CreateBooking;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.Modules.Identity;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Venues;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Time;
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

// 10. Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
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
            ValidAudience = opts.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256]
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

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

// 12. Dependency Injection
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<ITimeZoneProvider, CairoTimeZoneProvider>();
builder.Services.AddSingleton<IErrorMessageLocalizer, ErrorMessageLocalizer>();
builder.Services.AddMalabakValidation();
builder.Services.AddMalabakDataSeeders();
builder.Services.AddScoped<ICommandHandler<CreateBookingCommand, Result<CreateBookingResponse>>, CreateBookingHandler>();
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddVenuesModule();

var app = builder.Build();

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

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMalabakSecurity();
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

await app.RunAsync();

// Required for WebApplicationFactory in integration tests
public partial class Program
{
    protected Program() { }
}
