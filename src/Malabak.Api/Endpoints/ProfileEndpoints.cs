using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Media;
using Malabak.Modules.Identity.Application.Profile;
using Malabak.Modules.Identity.Domain;
using Malabak.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class ProfileEndpoints
{
    public static void MapProfileEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/me")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(MalabakAuthorization.AnySessionPolicy)
            .WithTags("Profile & Onboarding");

        group.MapGet("/onboarding", async (ClaimsPrincipal user, ProfileHandlers profiles, CancellationToken ct) =>
            await profiles.GetOnboardingStatusAsync(user.GetUserId()!.Value, ct) is { } status ? Results.Ok(status) : Results.Unauthorized())
        .Produces<OnboardingStatusDto>().WithName("GetOnboardingStatus").WithSummary("Whether the signed-in user still has onboarding steps left");

        group.MapPut("/onboarding/profile", async (ClaimsPrincipal user, [FromBody] CompleteProfileApiRequest request,
            ICommandHandler<CompleteProfileCommand, Result<MyProfileDto>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new CompleteProfileCommand(
                user.GetUserId()!.Value, request.FullName, request.City, request.Area, request.Positions, request.SkillLevel, request.Language), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: 400);
        })
        .WithName("CompleteProfile").WithSummary("Name, city/area, optional positions and skill level, language");

        group.MapPut("/language", async (ClaimsPrincipal user, [FromBody] LanguageApiRequest request,
            ICommandHandler<SetLanguageCommand, Result> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new SetLanguageCommand(user.GetUserId()!.Value, request.Language), ct);
            return result.IsSuccess ? Results.NoContent() : Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: 400);
        })
        .WithName("SetLanguage");

        group.MapGet("", async (ClaimsPrincipal user, ProfileHandlers profiles, CancellationToken ct) =>
            await profiles.GetMyProfileAsync(user.GetUserId()!.Value, ct) is { } profile ? Results.Ok(profile) : Results.Unauthorized())
        .Produces<MyProfileDto>().WithName("GetMyProfile").WithSummary("The signed-in user's own profile");

        group.MapPatch("", async (ClaimsPrincipal user, [FromBody] UpdateProfileApiRequest request,
            ICommandHandler<UpdateMyProfileCommand, Result<MyProfileDto>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new UpdateMyProfileCommand(
                user.GetUserId()!.Value, request.FullName, request.City, request.Area, request.Positions, request.SkillLevel, request.Language), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: 400);
        })
        .WithName("UpdateMyProfile").WithSummary("Partial update; phone and email change through their verified flows only");

        group.MapPut("/avatar", async (ClaimsPrincipal user, [FromBody] AvatarApiRequest request, MediaService media,
            ICommandHandler<SetAvatarCommand, Result<MyProfileDto>> handler, CancellationToken ct) =>
        {
            var userId = user.GetUserId()!.Value;
            if (await media.FindOwnedReadyAsync(request.MediaId, userId, MediaPurpose.Avatar, ct) is null)
            {
                return Results.Problem(title: "Media.NotFound", detail: "Upload the avatar first (POST /media/uploads with purpose Avatar).", statusCode: 400);
            }

            var result = await handler.HandleAsync(new SetAvatarCommand(userId, MediaService.PublicUrl(request.MediaId, "medium")), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: 400);
        })
        .WithName("SetAvatar").WithSummary("Attach an avatar uploaded through the media pipeline");

        group.MapDelete("/avatar", async (ClaimsPrincipal user, ICommandHandler<SetAvatarCommand, Result<MyProfileDto>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new SetAvatarCommand(user.GetUserId()!.Value, null), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: 400);
        })
        .WithName("RemoveAvatar");

        app.MapGroup("/api/v{version:apiVersion}/players")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization()
            .WithTags("Profile & Onboarding")
            .MapGet("/{userId:guid}", async (Guid userId, ProfileHandlers profiles, IPlayerStatsSummaryProvider stats, CancellationToken ct) =>
                await profiles.GetPublicProfileAsync(userId, stats, ct) is { } profile ? Results.Ok(profile) : Results.NotFound())
            .Produces<PublicPlayerProfileDto>().WithName("GetPlayerProfile").WithSummary("Privacy-safe public profile (no phone, email or exact location)");
    }

    public sealed record UpdateProfileApiRequest(
        string? FullName, string? City, string? Area, IReadOnlyList<PlayerPosition>? Positions, SkillLevel? SkillLevel, string? Language);

    public sealed record AvatarApiRequest(Guid MediaId);

    public sealed record CompleteProfileApiRequest(
        string FullName, string City, string Area, IReadOnlyList<PlayerPosition>? Positions, SkillLevel? SkillLevel, string? Language);

    public sealed record LanguageApiRequest(string Language);
}
