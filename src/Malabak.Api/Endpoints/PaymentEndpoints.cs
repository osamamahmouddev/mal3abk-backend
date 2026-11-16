using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Payments.Application.Services;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .WithTags("Payments");

        // 1. Initiate Paymob payment for booking commission / deposit
        group.MapPost("/bookings/{id:guid}/pay-commission", async (
            Guid id,
            ClaimsPrincipal user,
            [FromBody] PayCommissionRequest? request,
            IPaymentService paymentService,
            CancellationToken ct) =>
        {
            var playerId = user.GetUserId()!.Value;
            var firstName = user.FindFirst(ClaimTypes.GivenName)?.Value ?? request?.FirstName;
            var lastName = user.FindFirst(ClaimTypes.Surname)?.Value ?? request?.LastName;
            var phone = user.FindFirst(ClaimTypes.MobilePhone)?.Value ?? request?.PhoneNumber;
            var email = user.FindFirst(ClaimTypes.Email)?.Value ?? request?.Email;

            var result = await paymentService.InitiateBookingCommissionPaymentAsync(
                id, playerId, firstName, lastName, phone, email, ct);

            if (result.IsSuccess)
            {
                return Results.Ok(result.Value);
            }

            var status = result.Error.Code switch
            {
                var c when c.EndsWith("NotFound", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
                var c when c.EndsWith("AlreadyPaid", StringComparison.Ordinal) => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };

            return Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: status);
        })
        .RequireAuthorization(Permissions.BookingsCreate)
        .RequireDistributedRateLimiting("booking-payment", permitLimit: 20, windowSeconds: 60)
        .WithName("PayBookingCommission")
        .WithSummary("Creates a Paymob checkout intention for the platform commission / deposit");

        // 2. Paymob Webhook Callback
        group.MapPost("/payments/paymob/webhook", async (
            HttpRequest request,
            IPaymentService paymentService,
            CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var rawBody = await reader.ReadToEndAsync(ct);

            // Read HMAC from query string or header
            string? hmac = null;
            if (request.Query.TryGetValue("hmac", out var hmacQuery))
            {
                hmac = hmacQuery.ToString();
            }
            else if (request.Headers.TryGetValue("X-Paymob-Hmac", out var hmacHeader))
            {
                hmac = hmacHeader.ToString();
            }

            var result = await paymentService.ProcessPaymobWebhookAsync(rawBody, hmac, ct);
            if (result.IsFailure)
            {
                if (result.Error.Code == "Payment.InvalidHmac")
                {
                    return Results.Unauthorized();
                }

                return Results.BadRequest(new { error = result.Error.Description });
            }

            return Results.Ok(new { status = "success" });
        })
        .AllowAnonymous()
        .RequireDistributedRateLimiting("paymob-webhook", permitLimit: 120, windowSeconds: 60)
        .WithName("PaymobWebhook")
        .WithSummary("Receives asynchronous payment status notifications from Paymob");
    }

    public sealed record PayCommissionRequest(string? FirstName, string? LastName, string? PhoneNumber, string? Email);
}
