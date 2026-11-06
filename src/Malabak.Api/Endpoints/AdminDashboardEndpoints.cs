using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Identity.Application.Authorization;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public sealed record AdminDashboardDto(
    DateOnly From,
    DateOnly To,
    string TimeZone,
    IReadOnlyDictionary<string, decimal> Figures,
    IReadOnlyDictionary<string, IReadOnlyList<SeriesPoint>> Series,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Distributions);

/// <summary>
/// Admin dashboard KPIs (TASK-710). Every module contributes its figures (see each <see cref="IAdminMetricsProvider"/> for the
/// exact definition of its keys). The period is a range of Cairo calendar days, both ends included; "now" figures (totals,
/// open items, MRR) ignore the period. Money figures are only returned to staff who may see finance data.
/// </summary>
public static class AdminDashboardEndpoints
{
    public const int DefaultDays = 30;
    public const int MaxDays = 366;

    private static readonly string[] _financePrefixes = ["mrr", "revenue.", "subscriptions.", "bookings.confirmed_value"];

    public static void MapAdminDashboardEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        app.MapGet("/api/v{version:apiVersion}/admin/dashboard", async ([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, ClaimsPrincipal user, HttpContext http,
                IEnumerable<IAdminMetricsProvider> providers, IUserPermissionService permissions, IClock clock, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                var end = to ?? clock.CairoToday;
                var start = from ?? end.AddDays(-(DefaultDays - 1));
                if (start > end || end.DayNumber - start.DayNumber + 1 > MaxDays)
                {
                    return Results.Problem(title: "AdminDashboard.InvalidPeriod", detail: $"Choose a period of 1 to {MaxDays} days.", statusCode: StatusCodes.Status400BadRequest);
                }

                var granted = await permissions.GetAsync(user.GetUserId()!.Value, ct);
                var showFinance = granted?.Has(Permissions.InvoicesView) == true;
                var period = new MetricsPeriod(ToUtc(start, clock.CairoTimeZone), ToUtc(end.AddDays(1), clock.CairoTimeZone), clock.CairoTimeZone);
                return Results.Ok(await BuildAsync(providers, period, start, end, showFinance, ct));
            })
            .WithApiVersionSet(apiVersionSet).MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.AdminBookingsView)
            .AddEndpointFilter<AdminIpAllowlistFilter>()
            .RequireDistributedRateLimiting("admin-dashboard", permitLimit: 30, windowSeconds: 60)
            .Produces<AdminDashboardDto>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("Admin Dashboard")
            .WithName("AdminDashboard")
            .WithSummary("KPIs, daily growth series and distributions for a period of Cairo days (default: last 30 days)");
    }

    public static async Task<AdminDashboardDto> BuildAsync(
        IEnumerable<IAdminMetricsProvider> providers, MetricsPeriod period, DateOnly from, DateOnly to, bool showFinance, CancellationToken cancellationToken)
    {
        var figures = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
        var series = new SortedDictionary<string, IReadOnlyList<SeriesPoint>>(StringComparer.Ordinal);
        var distributions = new SortedDictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal);

        // Sequential on purpose: the providers share the request's scoped DbContexts, which are not thread-safe.
        foreach (var provider in providers)
        {
            var part = await provider.GetAsync(period, cancellationToken);
            Merge(part.Figures, figures, showFinance);
            Merge(part.Series, series, showFinance);
            Merge(part.Distributions, distributions, showFinance);
        }

        return new AdminDashboardDto(from, to, period.TimeZone.Id, figures, series, distributions);
    }

    private static void Merge<T>(IReadOnlyDictionary<string, T> source, SortedDictionary<string, T> target, bool showFinance)
    {
        foreach (var (key, value) in source)
        {
            if (showFinance || !_financePrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal)))
            {
                target[key] = value;
            }
        }
    }

    private static DateTime ToUtc(DateOnly cairoDay, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeToUtc(cairoDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);
}
