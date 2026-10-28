using Malabak.Modules.Identity.Application.StepUp;

namespace Malabak.Api.Infrastructure.Authorization;

/// <summary>
/// Requires a valid step-up token for <paramref name="scope"/> in the <c>X-Step-Up-Token</c> header
/// (e.g. <c>.RequireStepUp(StepUpScopes.DeleteAccount)</c> on account deletion).
/// </summary>
public sealed class StepUpEndpointFilter(string scope) : IEndpointFilter
{
    public const string HeaderName = "X-Step-Up-Token";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var userId = http.User.GetUserId();
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var verifier = http.RequestServices.GetRequiredService<IStepUpVerifier>();
        if (!await verifier.IsValidAsync(userId.Value, scope, http.Request.Headers[HeaderName].ToString(), http.RequestAborted))
        {
            return Results.Problem(
                title: "Identity.StepUpRequired",
                detail: $"Re-verify your identity (scope '{scope}') before this action.",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?> { ["stepUpScope"] = scope });
        }

        return await next(context);
    }
}

public static class StepUpEndpointExtensions
{
    public static TBuilder RequireStepUp<TBuilder>(this TBuilder builder, string scope)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new StepUpEndpointFilter(scope));
}
