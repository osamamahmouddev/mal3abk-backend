using Asp.Versioning.Builder;
using Malabak.SharedKernel.Reference;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>
/// Public, cacheable reference data for app forms and filters. Not sensitive, so anonymous; responses carry a
/// version-based ETag and a one-day cache so clients rarely hit the API for them.
/// </summary>
public static class ReferenceDataEndpoints
{
    public static void MapReferenceDataEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/reference")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .AllowAnonymous()
            .WithTags("Reference Data");

        group.MapGet("/governorates", (HttpContext http) => Cached(http, EgyptReferenceData.Governorates))
            .WithName("GetGovernorates").WithSummary("Egyptian governorates with curated areas for launch cities");

        group.MapGet("/facilities", (HttpContext http) => Cached(http, EgyptReferenceData.Facilities)).WithName("GetFacilities");

        group.MapGet("/pitch-types", (HttpContext http) => Cached(http, EgyptReferenceData.PitchTypes)).WithName("GetPitchTypes");

        group.MapGet("/surfaces", (HttpContext http) => Cached(http, EgyptReferenceData.Surfaces)).WithName("GetSurfaces");

        group.MapGet("/holidays", (HttpContext http, [FromQuery] int? year) =>
        {
            var target = year ?? DateTime.UtcNow.Year;
            return target is < 2000 or > 2100
                ? Results.Problem(title: "Reference.InvalidYear", statusCode: StatusCodes.Status400BadRequest)
                : Cached(http, EgyptHolidayCalendar.ForYear(target), $"-{target}");
        })
        .WithName("GetHolidays").WithSummary("Egyptian public holidays; Islamic dates are flagged as estimates");
    }

    private static IResult Cached<T>(HttpContext http, T payload, string etagSuffix = "")
    {
        var etag = $"\"ref-v{EgyptReferenceData.Version}{etagSuffix}\"";
        http.Response.Headers.CacheControl = "public, max-age=86400";
        http.Response.Headers.ETag = etag;

        return http.Request.Headers.IfNoneMatch.ToString() == etag ? Results.StatusCode(StatusCodes.Status304NotModified) : Results.Ok(payload);
    }
}
