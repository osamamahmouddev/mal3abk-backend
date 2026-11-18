using System.Text;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.Waitlist;
using Malabak.Modules.Identity.Domain;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public sealed record WaitlistApiRequest(
    string Phone,
    string Audience,
    string? ChosenPlan,
    string? TurnstileToken);

public static class WaitlistEndpoints
{
    public static void MapWaitlistEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        // Public waitlist registration (TASK-814)
        app.MapPost("/api/v{version:apiVersion}/waitlist", async (
            [FromBody] WaitlistApiRequest request,
            HttpContext http,
            IWaitlistService waitlist,
            ITurnstileValidator turnstile,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Phone))
            {
                return Results.Problem(title: "Waitlist.InvalidPhone", detail: "رقم الموبايل مطلوب.", statusCode: StatusCodes.Status400BadRequest);
            }

            var remoteIp = http.Connection.RemoteIpAddress?.ToString();

            // Validate Turnstile token if present or required
            if (!string.IsNullOrWhiteSpace(request.TurnstileToken))
            {
                var isValid = await turnstile.ValidateTokenAsync(request.TurnstileToken, remoteIp, ct);
                if (!isValid)
                {
                    return Results.Problem(title: "Waitlist.TurnstileFailed", detail: "فشل التحقق من أمان المتصفح (Turnstile).", statusCode: StatusCodes.Status400BadRequest);
                }
            }

            if (!Enum.TryParse<WaitlistAudience>(request.Audience, ignoreCase: true, out var audience))
            {
                return Results.Problem(title: "Waitlist.InvalidAudience", detail: "الجمهور يجب أن يكون player أو venue.", statusCode: StatusCodes.Status400BadRequest);
            }

            var userAgent = http.Request.Headers.UserAgent.ToString();
            var result = await waitlist.JoinAsync(
                request.Phone,
                audience,
                request.ChosenPlan,
                remoteIp,
                userAgent,
                ct);

            if (result.IsFailure)
            {
                return Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Ok(new
            {
                success = true,
                isNew = result.Value.IsNew,
                message = result.Value.Message
            });
        })
        .WithApiVersionSet(apiVersionSet)
        .MapToApiVersion(1, 0)
        .AllowAnonymous()
        .RequireDistributedRateLimiting("public-waitlist", permitLimit: 10, windowSeconds: 60)
        .WithTags("Waitlist")
        .WithName("JoinWaitlist")
        .WithSummary("Join the launch waitlist for players or venue owners");

        // Admin waitlist view and CSV export
        var admin = app.MapGroup("/api/v{version:apiVersion}/admin/waitlist")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.AdminUsersView)
            .WithTags("Admin Waitlist");

        admin.MapGet("", async (
            [FromQuery] string? audience,
            [FromQuery] string? plan,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromQuery] bool? export,
            HttpContext http,
            IWaitlistService waitlist,
            CancellationToken ct) =>
        {
            AdminResults.NoStore(http);

            WaitlistAudience? parsedAudience = null;
            if (!string.IsNullOrWhiteSpace(audience) && Enum.TryParse<WaitlistAudience>(audience, ignoreCase: true, out var parsed))
            {
                parsedAudience = parsed;
            }

            var isExport = export == true;
            var fetchSize = isExport ? 2000 : (pageSize ?? 50);
            var pageNumber = isExport ? 1 : (page ?? 1);

            var result = await waitlist.ListAsync(parsedAudience, plan, pageNumber, fetchSize, ct);
            if (result.IsFailure)
            {
                return AdminResults.Problem(result.Error);
            }

            if (isExport)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Id,Audience,ChosenPlan,Phone,ConsentRecordedAtUtc,CreatedAtUtc");
                foreach (var item in result.Value.Items)
                {
                    sb.AppendLine($"\"{item.Id}\",\"{item.Audience}\",\"{EscapeCsv(item.ChosenPlan)}\",\"{EscapeCsv(item.Phone)}\",\"{item.ConsentRecordedAtUtc:O}\",\"{item.CreatedAtUtc:O}\"");
                }

                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
                return Results.File(bytes, "text/csv; charset=utf-8", $"malabak-waitlist-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
            }

            return Results.Ok(result.Value);
        })
        .Produces<WaitlistPagedResult>()
        .WithName("GetAdminWaitlist")
        .WithSummary("View or export the landing page waitlist entries");

        admin.MapDelete("{id:guid}", async (
            Guid id,
            IWaitlistService waitlist,
            CancellationToken ct) =>
        {
            var result = await waitlist.DeleteAsync(id, ct);
            return result.IsFailure ? AdminResults.Problem(result.Error) : Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("DeleteAdminWaitlistEntry")
        .WithSummary("Delete a waitlist entry");
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("\"", "\"\"");
    }
}
