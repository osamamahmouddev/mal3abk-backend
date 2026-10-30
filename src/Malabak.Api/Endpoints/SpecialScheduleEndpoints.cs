using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Venues.Application.SpecialSchedules;
using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>
/// Closures, holidays, special hours, breaks, per-pitch weekly hours and booking rules. Changes that would leave
/// future bookings outside the schedule return 409 with the affected bookings until resent with confirm=true.
/// </summary>
public static class SpecialScheduleEndpoints
{
    public static void MapSpecialScheduleEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/venue-manager/venues/{venueId:guid}")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.VenuesManage)
            .AddEndpointFilter<VenueTenantEndpointFilter>()
            .WithTags("Special Schedules");

        group.MapGet("/special-schedules", async (Guid venueId, ClaimsPrincipal user, SpecialScheduleQueries queries, CancellationToken ct) =>
            ToResult(await queries.GetAsync(venueId, user.GetUserId()!.Value, ct)))
        .Produces<SpecialSchedulesDto>().WithName("GetSpecialSchedules").WithSummary("Upcoming exceptions, per-pitch weekly hours and booking rules");

        group.MapPost("/schedule-overrides", async (Guid venueId, ClaimsPrincipal user, [FromBody] ScheduleOverrideApiRequest request,
            ICommandHandler<AddScheduleOverrideCommand, Result<ScheduleChangeOutcome>> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new AddScheduleOverrideCommand(
                venueId, user.GetUserId()!.Value, request.PitchId, request.StartDate, request.EndDate ?? request.StartDate, request.Kind,
                request.FromTime, request.ToTime, request.Reason, request.Confirm), ct)))
        .WithName("AddScheduleOverride").WithSummary("Closure, special hours or break for one date (single occurrence) or a date range");

        group.MapDelete("/schedule-overrides/{overrideId:guid}", async (Guid venueId, Guid overrideId, ClaimsPrincipal user, [FromQuery] bool? confirm,
            ICommandHandler<RemoveScheduleOverrideCommand, Result<ScheduleChangeOutcome>> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new RemoveScheduleOverrideCommand(venueId, user.GetUserId()!.Value, overrideId, confirm ?? false), ct)))
        .WithName("RemoveScheduleOverride");

        group.MapPost("/schedule-overrides/holidays", async (Guid venueId, ClaimsPrincipal user, [FromBody] HolidayClosuresApiRequest request,
            ICommandHandler<ApplyHolidayClosuresCommand, Result<ScheduleChangeOutcome>> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new ApplyHolidayClosuresCommand(venueId, user.GetUserId()!.Value, request.Year, request.PitchId, request.Confirm), ct)))
        .WithName("ApplyHolidayClosures").WithSummary("Close on the remaining Egyptian public holidays of the year");

        group.MapPut("/pitches/{pitchId:guid}/weekly-hours", async (Guid venueId, Guid pitchId, ClaimsPrincipal user, [FromBody] PitchWeeklyHoursApiRequest request,
            ICommandHandler<SetPitchWeeklyHoursCommand, Result<ScheduleChangeOutcome>> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new SetPitchWeeklyHoursCommand(venueId, user.GetUserId()!.Value, pitchId, request.Days, request.Confirm), ct)))
        .WithName("SetPitchWeeklyHours").WithSummary("Recurring hours for one pitch; an empty list falls back to the venue hours");

        group.MapPost("/schedules/impact", async (Guid venueId, ClaimsPrincipal user, [FromBody] VenueWeeklyImpactApiRequest request,
            SpecialScheduleQueries queries, CancellationToken ct) =>
        {
            var result = await queries.PreviewVenueWeeklyChangeAsync(venueId, user.GetUserId()!.Value, request.Days, ct);
            return result.IsSuccess ? Results.Ok(new ScheduleImpactResponse(result.Value)) : Problem(result.Error);
        })
        .Produces<ScheduleImpactResponse>().WithName("PreviewVenueScheduleImpact").WithSummary("Future bookings a change to the venue weekly hours would leave outside the schedule");

        group.MapPut("/booking-rules", async (Guid venueId, ClaimsPrincipal user, [FromBody] BookingRulesApiRequest request,
            ICommandHandler<SetBookingRulesCommand, Result<BookingRulesDto>> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new SetBookingRulesCommand(
                venueId, user.GetUserId()!.Value, request.PitchId, request.MinDurationMinutes, request.MaxDurationMinutes,
                request.IncrementMinutes, request.MinNoticeMinutes, request.MaxAdvanceDays), ct)))
        .WithName("SetBookingRules").WithSummary("Duration, increments, minimum notice and advance window (venue default or per pitch)");

        group.MapGet("/pitches/{pitchId:guid}/availability", async (Guid venueId, Guid pitchId, [FromQuery] DateOnly date, ClaimsPrincipal user,
            SpecialScheduleQueries queries, CancellationToken ct) =>
            ToResult(await queries.GetPitchAvailabilityAsync(venueId, pitchId, date, user.GetUserId()!.Value, ct)))
        .Produces<PitchAvailabilityDto>().WithName("GetPitchAvailability").WithSummary("Open windows and bookable start times for a business date");
    }

    private static IResult ToResult(Result<ScheduleChangeOutcome> result)
    {
        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        return result.Value.Applied ? Results.Ok(result.Value) : Results.Conflict(result.Value);
    }

    private static IResult ToResult<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error);

    private static IResult Problem(Error error)
    {
        var status = error.Code.EndsWith("NotFound", StringComparison.Ordinal) ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
        return Results.Problem(title: error.Code, detail: error.Description, statusCode: status);
    }

    public sealed record ScheduleOverrideApiRequest(
        Guid? PitchId, DateOnly StartDate, DateOnly? EndDate, ScheduleOverrideKind Kind, TimeOnly? FromTime, TimeOnly? ToTime, string Reason, bool Confirm);

    public sealed record HolidayClosuresApiRequest(int Year, Guid? PitchId, bool Confirm);

    public sealed record PitchWeeklyHoursApiRequest(IReadOnlyList<WeeklyHoursInput> Days, bool Confirm);

    public sealed record VenueWeeklyImpactApiRequest(IReadOnlyList<VenueWeeklyHoursInput> Days);

    public sealed record BookingRulesApiRequest(
        Guid? PitchId, int MinDurationMinutes, int MaxDurationMinutes, int IncrementMinutes, int MinNoticeMinutes, int MaxAdvanceDays);

    public sealed record ScheduleImpactResponse(IReadOnlyList<Malabak.SharedKernel.Contracts.UpcomingBookingRef> AffectedBookings);
}
