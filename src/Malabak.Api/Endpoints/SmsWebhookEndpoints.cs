using Asp.Versioning.Builder;
using Malabak.Modules.Identity.Infrastructure.Sms;

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
    }
}
