using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
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
            .RequireAuthorization()
            .WithTags("Profile & Onboarding");

        group.MapGet("/onboarding", async (ClaimsPrincipal user, ProfileHandlers profiles, CancellationToken ct) =>
            await profiles.GetOnboardingStatusAsync(user.GetUserId()!.Value, ct) is { } status ? Results.Ok(status) : Results.Unauthorized())
        .WithName("GetOnboardingStatus").WithSummary("Whether the signed-in user still has onboarding steps left");

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
    }

    public sealed record CompleteProfileApiRequest(
        string FullName, string City, string Area, IReadOnlyList<PlayerPosition>? Positions, SkillLevel? SkillLevel, string? Language);

    public sealed record LanguageApiRequest(string Language);
}
