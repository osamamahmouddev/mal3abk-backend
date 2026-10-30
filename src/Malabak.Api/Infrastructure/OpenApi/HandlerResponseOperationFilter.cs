using Malabak.SharedKernel;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Malabak.Api.Infrastructure.OpenApi;

/// <summary>
/// Documents the success body of minimal API endpoints from the handler they inject: an endpoint taking
/// <c>ICommandHandler&lt;TCommand, Result&lt;T&gt;&gt;</c> or <c>IQueryHandler&lt;TQuery, Result&lt;T&gt;&gt;</c> returns
/// <c>T</c>. Endpoints that declare <c>.Produces&lt;T&gt;()</c> themselves are left as they are.
/// </summary>
public sealed class HandlerResponseOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        if (context.MethodInfo is null || operation.Responses.Any(r => r.Key.StartsWith('2') && r.Value.Content.Count > 0))
        {
            return;
        }

        var responseType = context.MethodInfo.GetParameters()
            .Select(p => FindResultValueType(p.ParameterType))
            .FirstOrDefault(t => t is not null);
        if (responseType is null)
        {
            return;
        }

        var key = operation.Responses.Keys.FirstOrDefault(k => k.StartsWith('2')) ?? "200";
        if (!operation.Responses.TryGetValue(key, out var response))
        {
            response = new OpenApiResponse { Description = "Success" };
            operation.Responses[key] = response;
        }

        response.Content["application/json"] = new OpenApiMediaType
        {
            Schema = context.SchemaGenerator.GenerateSchema(responseType, context.SchemaRepository),
        };
    }

    private static Type? FindResultValueType(Type parameterType)
    {
        if (!parameterType.IsGenericType)
        {
            return null;
        }

        var definition = parameterType.GetGenericTypeDefinition();
        if (definition != typeof(ICommandHandler<,>) && definition != typeof(IQueryHandler<,>))
        {
            return null;
        }

        var resultType = parameterType.GetGenericArguments()[1];
        return resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Result<>)
            ? resultType.GetGenericArguments()[0]
            : null;
    }
}
