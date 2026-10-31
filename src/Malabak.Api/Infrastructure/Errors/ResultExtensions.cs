using System.Diagnostics;
using Malabak.Api.Infrastructure.Localization;
using Malabak.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Api.Infrastructure.Errors;

public static class ResultExtensions
{
    public static IResult ToHttpResult(
        this Result result,
        HttpContext? httpContext = null,
        IErrorMessageLocalizer? localizer = null,
        Func<IResult>? onSuccess = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return onSuccess is not null ? onSuccess() : Results.NoContent();
        }

        return ToProblemHttpResult(result.Error, httpContext, localizer);
    }

    public static IResult ToHttpResult<TValue>(
        this Result<TValue> result,
        Func<TValue, IResult>? onSuccess = null,
        HttpContext? httpContext = null,
        IErrorMessageLocalizer? localizer = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return onSuccess is not null ? onSuccess(result.Value) : Results.Ok(result.Value);
        }

        return ToProblemHttpResult(result.Error, httpContext, localizer);
    }

    private static IResult ToProblemHttpResult(
        Error error,
        HttpContext? httpContext,
        IErrorMessageLocalizer? localizer)
    {
        var resolvedLocalizer = localizer ?? httpContext?.RequestServices.GetService<IErrorMessageLocalizer>();

        var (statusCode, rfcType) = error.Type switch
        {
            ErrorType.Validation => (StatusCodes.Status400BadRequest, "https://tools.ietf.org/html/rfc9110#section-15.5.1"),
            ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, "https://tools.ietf.org/html/rfc9110#section-15.5.2"),
            ErrorType.Forbidden => (StatusCodes.Status403Forbidden, "https://tools.ietf.org/html/rfc9110#section-15.5.4"),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "https://tools.ietf.org/html/rfc9110#section-15.5.5"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "https://tools.ietf.org/html/rfc9110#section-15.5.10"),
            ErrorType.Failure => (StatusCodes.Status422UnprocessableEntity, "https://tools.ietf.org/html/rfc9110#section-15.5.21"),
            _ => (StatusCodes.Status500InternalServerError, "https://tools.ietf.org/html/rfc9110#section-15.6.1")
        };

        var title = resolvedLocalizer?.GetLocalizedTitle(error.Type.ToString())
            ?? GetDefaultTitle(error.Type);

        var detail = resolvedLocalizer?.GetLocalizedMessage(error.Code, error.Description)
            ?? (string.IsNullOrWhiteSpace(error.Description) ? error.Code : error.Description);

        var traceId = Activity.Current?.Id ?? httpContext?.TraceIdentifier;

        var extensions = new Dictionary<string, object?>
        {
            ["code"] = error.Code
        };

        if (!string.IsNullOrEmpty(traceId))
        {
            extensions["traceId"] = traceId;
        }

        return Results.Problem(
            detail: detail,
            instance: httpContext?.Request.Path,
            statusCode: statusCode,
            title: title,
            type: rfcType,
            extensions: extensions);
    }

    private static string GetDefaultTitle(ErrorType errorType) => errorType switch
    {
        ErrorType.Validation => "Validation Error",
        ErrorType.Unauthorized => "Unauthorized",
        ErrorType.Forbidden => "Forbidden",
        ErrorType.NotFound => "Not Found",
        ErrorType.Conflict => "Conflict",
        ErrorType.Failure => "Unprocessable Entity",
        _ => "Internal Server Error"
    };
}
