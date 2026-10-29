using System.Diagnostics;
using FluentValidation;
using Malabak.Api.Infrastructure.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Api.Infrastructure.Validation;

public sealed class ValidationEndpointFilter<TRequest> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var argument = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (argument is null)
        {
            return await next(context);
        }

        var validator = context.HttpContext.RequestServices.GetService<IValidator<TRequest>>();
        if (validator is null)
        {
            return await next(context);
        }

        var validationResult = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
        if (!validationResult.IsValid)
        {
            var localizer = context.HttpContext.RequestServices.GetService<IErrorMessageLocalizer>();
            var errors = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => localizer?.GetLocalizedMessage(e.ErrorCode, e.ErrorMessage) ?? e.ErrorMessage).ToArray());

            var traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            var title = localizer?.GetLocalizedTitle("Validation") ?? "Validation Error";

            return Results.ValidationProblem(
                errors,
                detail: localizer?.GetLocalizedMessage("validation_failed", "One or more validation errors occurred.") ?? "One or more validation errors occurred.",
                instance: context.HttpContext.Request.Path,
                statusCode: StatusCodes.Status400BadRequest,
                title: title,
                type: "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "validation_failed",
                    ["traceId"] = traceId
                });
        }

        return await next(context);
    }
}
