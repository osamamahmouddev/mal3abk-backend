using Malabak.SharedKernel;

namespace Malabak.Api.Endpoints;

/// <summary>Maps admin service results to responses from the error type (validation 400, not found 404, conflict 409, forbidden 403).</summary>
internal static class AdminResults
{
    public static IResult From<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error);

    public static IResult From(Result result) => result.IsSuccess ? Results.NoContent() : Problem(result.Error);

    public static IResult Problem(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(title: error.Code, detail: error.Description, statusCode: status);
    }

    public static void NoStore(HttpContext http) => http.Response.Headers.CacheControl = "private, no-store";
}
