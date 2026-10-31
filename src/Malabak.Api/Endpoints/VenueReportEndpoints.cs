using System.Globalization;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Booking.Application.Reports;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>Venue reports and CSV export (TASK-504). Requires the reports permission at the venue.</summary>
public static class VenueReportEndpoints
{
    public static void MapVenueReportEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var reports = app.MapGroup("/api/v{version:apiVersion}/venue-manager/venues/{venueId:guid}/reports")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.VenuesManage)
            .AddEndpointFilter<VenueTenantEndpointFilter>()
            .AddEndpointFilter(new VenueAccessEndpointFilter(Permissions.ReportsView))
            .WithTags("Venue Reports");

        reports.MapGet("", async (Guid venueId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? pitchId,
            [FromQuery] ReportGrouping? groupBy, HttpContext http, VenueReportService service, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var result = await service.GetAsync(venueId, from, to, pitchId, groupBy ?? ReportGrouping.Day, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error);
        })
        .RequireDistributedRateLimiting("venue-reports", permitLimit: 30, windowSeconds: 60)
        .WithName("GetVenueReport")
        .WithSummary("Bookings per day/week/month, status distribution, revenue split, pitch utilization and peak hours");

        reports.MapGet("/bookings.csv", async (Guid venueId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? pitchId,
            HttpContext http, VenueReportService service, CancellationToken ct) =>
        {
            var result = await service.ExportCsvAsync(venueId, from, to, pitchId, ct);
            if (result.IsFailure)
            {
                return Problem(result.Error);
            }

            http.Response.Headers.CacheControl = "private, no-store";
            var name = string.Create(CultureInfo.InvariantCulture, $"bookings-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
            return Results.File(result.Value, "text/csv; charset=utf-8", name);
        })
        .RequireDistributedRateLimiting("venue-reports-export", permitLimit: 10, windowSeconds: 60)
        .WithName("ExportVenueBookingsCsv")
        .WithSummary("Bookings in the range as CSV (up to 20,000 rows)");
    }

    private static IResult Problem(Error error) =>
        Results.Problem(title: error.Code, detail: error.Description, statusCode: StatusCodes.Status400BadRequest);
}
