using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Venues.Application.BlockedSlots;
using Malabak.Modules.Venues.Application.BlockedSlots.CreateBlockedSlot;
using Malabak.Modules.Venues.Application.BlockedSlots.DeleteBlockedSlot;
using Malabak.Modules.Venues.Application.BlockedSlots.GetBlockedSlotsByVenue;
using Malabak.Modules.Venues.Application.Pitches;
using Malabak.Modules.Venues.Application.Pitches.CreatePitch;
using Malabak.Modules.Venues.Application.Pitches.DeletePitch;
using Malabak.Modules.Venues.Application.Pitches.GetPitchById;
using Malabak.Modules.Venues.Application.Pitches.GetPitchesByVenue;
using Malabak.Modules.Venues.Application.Pitches.UpdatePitch;
using Malabak.Modules.Venues.Application.Schedules;
using Malabak.Modules.Venues.Application.Schedules.GetVenueSchedules;
using Malabak.Modules.Venues.Application.Schedules.GetVenueSlots;
using Malabak.Modules.Venues.Application.Schedules.UpdateVenueSchedules;
using Malabak.Modules.Venues.Application.Venues;
using Malabak.Modules.Venues.Application.Venues.CreateVenue;
using Malabak.Modules.Venues.Application.Venues.DeleteVenue;
using Malabak.Modules.Venues.Application.Venues.GetVenueById;
using Malabak.Modules.Venues.Application.Venues.GetVenuesByOwner;
using Malabak.Modules.Venues.Application.Venues.PublishVenue;
using Malabak.Modules.Venues.Application.Venues.UpdateVenue;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class VenueManagerEndpoints
{
    public static void MapVenueManagerEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/venue-manager")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.VenuesManage)
            .AddEndpointFilter<VenueTenantEndpointFilter>()
            .WithTags("Venue Manager");

        // --- Venues ---

        group.MapPost("/venues", async (
            ClaimsPrincipal user,
            [FromBody] CreateVenueApiRequest request,
            ICommandHandler<CreateVenueCommand, Result<VenueDto>> handler,
            CancellationToken cancellationToken) =>
        {
            var ownerId = GetUserId(user);
            if (!ownerId.HasValue)
            {
                return Results.Unauthorized();
            }

            var command = new CreateVenueCommand(
                OwnerId: ownerId.Value,
                Name: request.Name,
                Description: request.Description,
                Address: request.Address,
                City: request.City,
                Area: request.Area,
                Latitude: request.Latitude,
                Longitude: request.Longitude,
                PhoneNumber: request.PhoneNumber,
                Facilities: request.Facilities,
                Photos: request.Photos);

            var result = await handler.HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblem(result.Error);
            }

            return Results.Created($"/api/v1/venue-manager/venues/{result.Value.Id}", result.Value);
        })
        .WithName("CreateVenue")
        .WithSummary("Create a new venue for the current owner");

        group.MapGet("/venues", async (
            ClaimsPrincipal user,
            IQueryHandler<GetVenuesByOwnerQuery, Result<IReadOnlyList<VenueDto>>> handler,
            CancellationToken cancellationToken) =>
        {
            var ownerId = GetUserId(user);
            if (!ownerId.HasValue)
            {
                return Results.Unauthorized();
            }

            var query = new GetVenuesByOwnerQuery(ownerId.Value);
            var result = await handler.HandleAsync(query, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblem(result.Error);
            }

            return Results.Ok(result.Value);
        })
        .WithName("GetVenuesByOwner")
        .WithSummary("Get all venues belonging to the current owner");

        group.MapGet("/venues/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            IQueryHandler<GetVenueByIdQuery, Result<VenueDto>> handler,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            if (!userId.HasValue)
            {
                return Results.Unauthorized();
            }

            var query = new GetVenueByIdQuery(id, userId.Value);
            var result = await handler.HandleAsync(query, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblem(result.Error);
            }

            return Results.Ok(result.Value);
        })
        .WithName("GetVenueById")
        .WithSummary("Get venue details by ID");

        group.MapPut("/venues/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            [FromBody] UpdateVenueApiRequest request,
            ICommandHandler<UpdateVenueCommand, Result<VenueDto>> handler,
            CancellationToken cancellationToken) =>
        {
            var ownerId = GetUserId(user);
            if (!ownerId.HasValue)
            {
                return Results.Unauthorized();
            }

            var command = new UpdateVenueCommand(
                VenueId: id,
                OwnerId: ownerId.Value,
                Name: request.Name,
                Description: request.Description,
                Address: request.Address,
                City: request.City,
                Area: request.Area,
                Latitude: request.Latitude,
                Longitude: request.Longitude,
                PhoneNumber: request.PhoneNumber,
                Facilities: request.Facilities,
                Photos: request.Photos);

            var result = await handler.HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblem(result.Error);
            }

            return Results.Ok(result.Value);
        })
        .WithName("UpdateVenue")
        .WithSummary("Update venue details");

        group.MapDelete("/venues/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            ICommandHandler<DeleteVenueCommand, Result> handler,
            CancellationToken cancellationToken) =>
        {
            var ownerId = GetUserId(user);
            if (!ownerId.HasValue)
            {
                return Results.Unauthorized();
            }

            var command = new DeleteVenueCommand(id, ownerId.Value);
            var result = await handler.HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblem(result.Error);
            }

            return Results.NoContent();
        })
        .WithName("DeleteVenue")
        .WithSummary("Soft delete a venue");

        group.MapPost("/venues/{id:guid}/publish", async (
            Guid id,
            ClaimsPrincipal user,
            [FromBody] PublishVenueApiRequest request,
            ICommandHandler<PublishVenueCommand, Result> handler,
            CancellationToken cancellationToken) =>
        {
            var ownerId = GetUserId(user);
            if (!ownerId.HasValue)
            {
                return Results.Unauthorized();
            }

            var command = new PublishVenueCommand(id, ownerId.Value, request.IsPublished);
            var result = await handler.HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblem(result.Error);
            }

            return Results.Ok(new { message = request.IsPublished ? "Venue published successfully." : "Venue unpublished successfully." });
        })
        .WithName("PublishVenue")
        .WithSummary("Publish or unpublish a venue (requires prior approval)");

        // --- Pitches ---

        group.MapPost("/venues/{venueId:guid}/pitches", async (
            Guid venueId,
            ClaimsPrincipal user,
            [FromBody] CreatePitchApiRequest request,
            ICommandHandler<CreatePitchCommand, Result<PitchDto>> handler,
            CancellationToken cancellationToken) =>
        {
            var ownerId = GetUserId(user);
            if (!ownerId.HasValue)
            {
                return Results.Unauthorized();
            }

            var command = new CreatePitchCommand(
                VenueId: venueId,
                OwnerId: ownerId.Value,
                Name: request.Name,
                TurfType: request.TurfType,
                Capacity: request.Capacity,
                LengthMeters: request.LengthMeters,
                WidthMeters: request.WidthMeters,
                Photos: request.Photos);

            var result = await handler.HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblem(result.Error);
            }

            return Results.Created($"/api/v1/venue-manager/pitches/{result.Value.Id}", result.Value);
        })
        .WithName("CreatePitch")
        .WithSummary("Create a pitch under a venue");

        group.MapGet("/venues/{venueId:guid}/pitches", async (
            Guid venueId,
            ClaimsPrincipal user,
            IQueryHandler<GetPitchesByVenueQuery, Result<IReadOnlyList<PitchDto>>> handler,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            if (!userId.HasValue)
            {
                return Results.Unauthorized();
            }

            var query = new GetPitchesByVenueQuery(venueId, userId.Value);
            var result = await handler.HandleAsync(query, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblem(result.Error);
            }

            return Results.Ok(result.Value);
        })
        .WithName("GetPitchesByVenue")
        .WithSummary("Get all pitches for a specific venue");

        group.MapGet("/pitches/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            IQueryHandler<GetPitchByIdQuery, Result<PitchDto>> handler,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            if (!userId.HasValue)
            {
                return Results.Unauthorized();
            }

            var query = new GetPitchByIdQuery(id, userId.Value);
            var result = await handler.HandleAsync(query, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblem(result.Error);
            }

            return Results.Ok(result.Value);
        })
        .WithName("GetPitchById")
        .WithSummary("Get pitch details by ID");

        group.MapPut("/pitches/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            [FromBody] UpdatePitchApiRequest request,
            ICommandHandler<UpdatePitchCommand, Result<PitchDto>> handler,
            CancellationToken cancellationToken) =>
        {
            var ownerId = GetUserId(user);
            if (!ownerId.HasValue)
            {
                return Results.Unauthorized();
            }

            var command = new UpdatePitchCommand(
                PitchId: id,
                OwnerId: ownerId.Value,
                Name: request.Name,
                TurfType: request.TurfType,
                Capacity: request.Capacity,
                LengthMeters: request.LengthMeters,
                WidthMeters: request.WidthMeters,
                Photos: request.Photos);

            var result = await handler.HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblem(result.Error);
            }

            return Results.Ok(result.Value);
        })
        .WithName("UpdatePitch")
        .WithSummary("Update pitch details");

        group.MapDelete("/pitches/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            ICommandHandler<DeletePitchCommand, Result> handler,
            CancellationToken cancellationToken) =>
        {
            var ownerId = GetUserId(user);
            if (!ownerId.HasValue)
            {
                return Results.Unauthorized();
            }

            var command = new DeletePitchCommand(id, ownerId.Value);
            var result = await handler.HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblem(result.Error);
            }

            return Results.NoContent();
        })
        .WithName("DeletePitch")
        .WithSummary("Soft delete a pitch");

        // --- Operating Schedules & Slots ---

        group.MapPut("/venues/{venueId:guid}/schedules", async (
            Guid venueId,
            ClaimsPrincipal user,
            [FromBody] UpdateVenueSchedulesApiRequest request,
            ICommandHandler<UpdateVenueSchedulesCommand, Result<IReadOnlyList<OperatingScheduleDto>>> handler,
            CancellationToken cancellationToken) =>
        {
            var ownerId = GetUserId(user);
            if (!ownerId.HasValue)
            {
                return Results.Unauthorized();
            }

            var items = request.Schedules
                .Select(s => new ScheduleItemInput(s.DayOfWeek, s.OpenTime, s.CloseTime, s.SlotDurationMinutes, s.IsActive))
                .ToList();

            var command = new UpdateVenueSchedulesCommand(venueId, ownerId.Value, items);
            var result = await handler.HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblem(result.Error);
            }

            return Results.Ok(result.Value);
        })
        .WithName("UpdateVenueSchedules")
        .WithSummary("Configure weekly operating schedules with overnight span support");

        group.MapGet("/venues/{venueId:guid}/schedules", async (
            Guid venueId,
            ClaimsPrincipal user,
            IQueryHandler<GetVenueSchedulesQuery, Result<IReadOnlyList<OperatingScheduleDto>>> handler,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            if (!userId.HasValue)
            {
                return Results.Unauthorized();
            }

            var query = new GetVenueSchedulesQuery(venueId, userId.Value);
            var result = await handler.HandleAsync(query, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblem(result.Error);
            }

            return Results.Ok(result.Value);
        })
        .WithName("GetVenueSchedules")
        .WithSummary("Get weekly operating schedules for a venue");

        group.MapGet("/venues/{venueId:guid}/slots", async (
            Guid venueId,
            [FromQuery] DateOnly date,
            ClaimsPrincipal user,
            IQueryHandler<GetVenueSlotsQuery, Result<IReadOnlyList<ScheduleSlotDto>>> handler,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            if (!userId.HasValue)
            {
                return Results.Unauthorized();
            }

            var query = new GetVenueSlotsQuery(venueId, date, userId.Value);
            var result = await handler.HandleAsync(query, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblem(result.Error);
            }

            return Results.Ok(result.Value);
        })
        .WithName("GetVenueSlots")
        .WithSummary("Get calculated bookable slots for a venue on a specific business date");

        // --- Blocked Slots ---

        group.MapPost("/venues/{venueId:guid}/blocked-slots", async (
            Guid venueId,
            ClaimsPrincipal user,
            [FromBody] CreateBlockedSlotApiRequest request,
            ICommandHandler<CreateBlockedSlotCommand, Result<BlockedSlotDto>> handler,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            if (!userId.HasValue)
            {
                return Results.Unauthorized();
            }

            var command = new CreateBlockedSlotCommand(
                VenueId: venueId,
                PitchId: request.PitchId,
                UserId: userId.Value,
                StartUtc: request.StartUtc,
                EndUtc: request.EndUtc,
                Reason: request.Reason);

            var result = await handler.HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblem(result.Error);
            }

            return Results.Created($"/api/v1/venue-manager/venues/{venueId}/blocked-slots/{result.Value.Id}", result.Value);
        })
        .WithName("CreateBlockedSlot")
        .WithSummary("Block pitch slots for maintenance or private events");

        group.MapGet("/venues/{venueId:guid}/blocked-slots", async (
            Guid venueId,
            ClaimsPrincipal user,
            IQueryHandler<GetBlockedSlotsByVenueQuery, Result<IReadOnlyList<BlockedSlotDto>>> handler,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            if (!userId.HasValue)
            {
                return Results.Unauthorized();
            }

            var query = new GetBlockedSlotsByVenueQuery(venueId, userId.Value);
            var result = await handler.HandleAsync(query, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblem(result.Error);
            }

            return Results.Ok(result.Value);
        })
        .WithName("GetBlockedSlotsByVenue")
        .WithSummary("Get all active blocked slots for a venue");

        group.MapDelete("/venues/{venueId:guid}/blocked-slots/{id:guid}", async (
            Guid venueId,
            Guid id,
            ClaimsPrincipal user,
            ICommandHandler<DeleteBlockedSlotCommand, Result> handler,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            if (!userId.HasValue)
            {
                return Results.Unauthorized();
            }

            var command = new DeleteBlockedSlotCommand(venueId, id, userId.Value);
            var result = await handler.HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblem(result.Error);
            }

            return Results.NoContent();
        })
        .WithName("DeleteBlockedSlot")
        .WithSummary("Remove a maintenance or private event block from a pitch slot");
    }

    private static IResult MapErrorToProblem(Error error)
    {
        var statusCode = error.Code switch
        {
            var code when code.EndsWith(".NotFound", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status404NotFound,
            var code when code.EndsWith(".Forbidden", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status403Forbidden,
            var code when code.EndsWith(".Conflict", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status409Conflict,
            var code when code.EndsWith(".Overlap", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status409Conflict,
            var code when code.EndsWith(".ConflictingBookings", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        return Results.Problem(
            title: error.Code,
            detail: error.Description,
            statusCode: statusCode);
    }

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var claimValue = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;

        return Guid.TryParse(claimValue, out var guid) ? guid : null;
    }

    public sealed record CreateVenueApiRequest(
        string Name,
        string? Description,
        string Address,
        string City,
        string Area,
        double? Latitude,
        double? Longitude,
        string PhoneNumber,
        IEnumerable<string>? Facilities,
        IEnumerable<string>? Photos);

    public sealed record UpdateVenueApiRequest(
        string Name,
        string? Description,
        string Address,
        string City,
        string Area,
        double? Latitude,
        double? Longitude,
        string PhoneNumber,
        IEnumerable<string>? Facilities,
        IEnumerable<string>? Photos);

    public sealed record PublishVenueApiRequest(bool IsPublished);

    public sealed record CreatePitchApiRequest(
        string Name,
        string TurfType,
        int Capacity,
        decimal? LengthMeters,
        decimal? WidthMeters,
        IEnumerable<string>? Photos);

    public sealed record UpdatePitchApiRequest(
        string Name,
        string TurfType,
        int Capacity,
        decimal? LengthMeters,
        decimal? WidthMeters,
        IEnumerable<string>? Photos);

    public sealed record ScheduleItemApiRequest(
        DayOfWeek DayOfWeek,
        TimeOnly OpenTime,
        TimeOnly CloseTime,
        int SlotDurationMinutes = 60,
        bool IsActive = true);

    public sealed record UpdateVenueSchedulesApiRequest(IReadOnlyList<ScheduleItemApiRequest> Schedules);

    public sealed record CreateBlockedSlotApiRequest(
        Guid PitchId,
        DateTime StartUtc,
        DateTime EndUtc,
        string Reason);
}
