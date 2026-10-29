using Asp.Versioning.Builder;
using Malabak.Modules.Identity.Infrastructure.Sms;
using Malabak.Modules.Identity.Infrastructure.Services;

namespace Malabak.Api.Endpoints;

public static class SmsWebhookEndpoints
{
    public static void MapSmsWebhookEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        // Anonymous by necessity (called by the provider); trust comes only from the verified signature.
        app.MapPost("/api/v{version:apiVersion}/webhooks/sms/twilio/{account}", async (string account, HttpRequest request,
            SmsDeliveryReceipts receipts, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest();
            }

            var form = await request.ReadFormAsync(ct);
            var fields = form.ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.Ordinal);

            var result = await receipts.ApplyTwilioAsync(account, fields, request.Headers["X-Twilio-Signature"].ToString(), ct);
            return result switch
            {
                DeliveryReceiptResult.InvalidSignature or DeliveryReceiptResult.UnknownAccount => Results.StatusCode(StatusCodes.Status403Forbidden),
                _ => Results.NoContent() // unknown message ids are acknowledged so the provider stops retrying
            };
        })
        .WithApiVersionSet(apiVersionSet)
        .MapToApiVersion(1, 0)
        .AllowAnonymous()
        .DisableAntiforgery()
        .WithTags("Webhooks")
        .WithName("TwilioDeliveryReceipt");

        // Email bounces/complaints from the mail provider (or a small relay that normalizes its events).
        app.MapPost("/api/v{version:apiVersion}/webhooks/email/bounce", async (HttpRequest request, EmailBounceHandler bounces, CancellationToken ct) =>
        {
            request.EnableBuffering();
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var raw = await reader.ReadToEndAsync(ct);
            if (raw.Length > 16_384)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            BounceEvent? evt;
            try
            {
                evt = System.Text.Json.JsonSerializer.Deserialize<BounceEvent>(raw, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest();
            }

            var result = await bounces.ApplyAsync(raw, request.Headers["X-Malabak-Signature"].ToString(), evt?.Email ?? string.Empty, evt?.Type ?? string.Empty, ct);
            return result == BounceResult.InvalidSignature ? Results.StatusCode(StatusCodes.Status403Forbidden) : Results.NoContent();
        })
        .WithApiVersionSet(apiVersionSet)
        .MapToApiVersion(1, 0)
        .AllowAnonymous()
        .DisableAntiforgery()
        .WithTags("Webhooks")
        .WithName("EmailBounce")
        .WithSummary("Signed (HMAC-SHA256, X-Malabak-Signature) bounce/complaint events; hard bounces and complaints are suppressed");
    }
}

internal sealed record BounceEvent(string? Email, string? Type);
