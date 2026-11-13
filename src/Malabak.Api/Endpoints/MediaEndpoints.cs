using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Media;
using Malabak.Modules.Venues.Application.Gallery;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class MediaEndpoints
{
    public const string UploadRateLimitPolicy = "media-upload";

    public static void MapMediaEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var media = app.MapGroup("/api/v{version:apiVersion}/media")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .WithTags("Media");

        media.MapPost("/uploads", async (ClaimsPrincipal user, [FromBody] CreateUploadApiRequest request, MediaService service, CancellationToken ct) =>
            Enum.IsDefined(request.Purpose)
                ? Results.Ok(await service.CreateUploadAsync(user.GetUserId()!.Value, request.Purpose, ct))
                : Results.Problem(title: "Media.InvalidPurpose", statusCode: StatusCodes.Status400BadRequest))
        .RequireAuthorization()
        .RequireRateLimiting(UploadRateLimitPolicy)
        .Produces<UploadTicket>().WithName("CreateMediaUpload").WithSummary("Get a single-use upload URL valid for 5 minutes");

        media.MapPut("/uploads/{id:guid}", async (Guid id, [FromQuery] string token, ClaimsPrincipal user, HttpRequest request,
            MediaService service, CancellationToken ct) =>
        {
            if (request.ContentLength is > ImageProcessor.MaxBytes)
            {
                return Results.Problem(title: "Media.TooLarge", statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);
            if (buffer.Length > ImageProcessor.MaxBytes)
            {
                return Results.Problem(title: "Media.TooLarge", statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            var result = await service.UploadAsync(id, user.GetUserId()!.Value, token, buffer.ToArray(), ct);
            return result.Success
                ? Results.Ok(new { result.MediaId, result.Urls })
                : Results.Problem(title: "Media.Rejected", detail: result.Error, statusCode: StatusCodes.Status400BadRequest);
        })
        .RequireAuthorization()
        .RequireRateLimiting(UploadRateLimitPolicy)
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(ImageProcessor.MaxBytes + 1024))
        .WithName("UploadMedia").WithSummary("Upload JPEG/PNG/WebP bytes (max 8 MB), or a PDF for payment proofs; images are stored only as re-encoded WebP variants, PDFs as validated bytes");

        media.MapGet("/{id:guid}/{variant}", async (Guid id, string variant, [FromQuery] string? exp, [FromQuery] string? sig,
            HttpContext http, MediaService service, CancellationToken ct) =>
        {
            var content = await service.GetVariantAsync(id, variant, exp, sig, ct);
            if (content is null)
            {
                return Results.NotFound();
            }

            // Variants are immutable (new upload = new id), so public ones can be cached by the CDN forever.
            http.Response.Headers.CacheControl = content.IsPublic ? "public, max-age=31536000, immutable" : "private, no-store";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            if (content.IsDocument)
            {
                // Never rendered in the browser: a download only, with a CSP that blocks anything the file could run.
                http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
                return Results.File(content.Bytes, content.ContentType, fileDownloadName: $"payment-proof-{id:N}.pdf");
            }

            return Results.File(content.Bytes, content.ContentType);
        })
        .AllowAnonymous()
        .WithName("GetMedia").WithSummary("Public variants are CDN-cacheable; private ones need a signed URL");

        // --- Venue & pitch galleries ---
        var gallery = app.MapGroup("/api/v{version:apiVersion}/venue-manager/venues/{venueId:guid}")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.VenuesManage)
            .AddEndpointFilter<VenueTenantEndpointFilter>()
            .WithTags("Venue Gallery");

        gallery.MapPost("/gallery", (Guid venueId, ClaimsPrincipal user, [FromBody] GalleryPhotoApiRequest request, MediaService service,
            ICommandHandler<AddGalleryPhotoCommand, Result<IReadOnlyList<string>>> handler, CancellationToken ct) =>
            AttachAsync(venueId, null, MediaPurpose.VenuePhoto, user, request, service, handler, ct))
        .WithName("AddVenuePhoto");

        gallery.MapDelete("/gallery/{mediaId:guid}", async (Guid venueId, Guid mediaId, ClaimsPrincipal user, MediaService service,
            ICommandHandler<RemoveGalleryPhotoCommand, Result<IReadOnlyList<string>>> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new RemoveGalleryPhotoCommand(venueId, null, user.GetUserId()!.Value, MediaService.PublicUrl(mediaId)), ct)))
        .WithName("RemoveVenuePhoto");

        gallery.MapPost("/pitches/{pitchId:guid}/gallery", (Guid venueId, Guid pitchId, ClaimsPrincipal user, [FromBody] GalleryPhotoApiRequest request,
            MediaService service, ICommandHandler<AddGalleryPhotoCommand, Result<IReadOnlyList<string>>> handler, CancellationToken ct) =>
            AttachAsync(venueId, pitchId, MediaPurpose.PitchPhoto, user, request, service, handler, ct))
        .WithName("AddPitchPhoto");

        gallery.MapDelete("/pitches/{pitchId:guid}/gallery/{mediaId:guid}", async (Guid venueId, Guid pitchId, Guid mediaId, ClaimsPrincipal user,
            MediaService service, ICommandHandler<RemoveGalleryPhotoCommand, Result<IReadOnlyList<string>>> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new RemoveGalleryPhotoCommand(venueId, pitchId, user.GetUserId()!.Value, MediaService.PublicUrl(mediaId)), ct)))
        .WithName("RemovePitchPhoto");
    }

    private static async Task<IResult> AttachAsync(Guid venueId, Guid? pitchId, MediaPurpose purpose, ClaimsPrincipal user,
        GalleryPhotoApiRequest request, MediaService service, ICommandHandler<AddGalleryPhotoCommand, Result<IReadOnlyList<string>>> handler,
        CancellationToken ct)
    {
        var userId = user.GetUserId()!.Value;

        // Only the uploader's own, fully processed image of the right purpose can be attached.
        var asset = await service.FindOwnedReadyAsync(request.MediaId, userId, purpose, ct);
        if (asset is null)
        {
            return Results.Problem(title: "Media.NotFound", statusCode: StatusCodes.Status404NotFound);
        }

        return ToResult(await handler.HandleAsync(new AddGalleryPhotoCommand(venueId, pitchId, userId, MediaService.PublicUrl(asset.Id)), ct));
    }

    private static IResult ToResult(Result<IReadOnlyList<string>> result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        var status = result.Error.Code.EndsWith("NotFound", StringComparison.Ordinal) ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict;
        return Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: status);
    }

    public sealed record CreateUploadApiRequest(MediaPurpose Purpose);

    public sealed record GalleryPhotoApiRequest(Guid MediaId);
}
