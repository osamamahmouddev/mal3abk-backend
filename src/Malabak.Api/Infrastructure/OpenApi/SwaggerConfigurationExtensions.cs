using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Malabak.Api.Infrastructure.OpenApi;

public static class SwaggerConfigurationExtensions
{
    public static IServiceCollection AddMalabakSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Malabak API",
                Version = "v1",
                Description = "High-performance modular monolith backend for the Malabak sports venue and pitch booking platform.",
                Contact = new OpenApiContact
                {
                    Name = "Malabak Engineering Team",
                    Email = "security@malabak.app"
                }
            });

            // ES256 Bearer JWT Authentication
            options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme. Format: 'Bearer {token}'",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT"
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = JwtBearerDefaults.AuthenticationScheme
                        }
                    },
                    Array.Empty<string>()
                }
            });

            // Non-nullable members are required in the schema, so the generated web client types match C# nullability.
            options.SupportNonNullableReferenceTypes();
            options.NonNullableReferenceTypesAsRequired();
            options.OperationFilter<HandlerResponseOperationFilter>();
            options.OperationFilter<StandardProblemDetailsOperationFilter>();
        });

        return services;
    }
}

public sealed class StandardProblemDetailsOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        operation.Responses.TryAdd("400", new OpenApiResponse { Description = "Bad Request (Validation failure, invalid inputs)" });
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Unauthorized (Missing or expired JWT access token)" });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Forbidden (Insufficient role or permissions)" });
        operation.Responses.TryAdd("404", new OpenApiResponse { Description = "Not Found (Resource does not exist)" });
        operation.Responses.TryAdd("409", new OpenApiResponse { Description = "Conflict (Concurrent update or duplicate idempotency in flight)" });
        operation.Responses.TryAdd("422", new OpenApiResponse { Description = "Unprocessable Entity (Business rule violation or payload mismatch)" });
        operation.Responses.TryAdd("429", new OpenApiResponse { Description = "Too Many Requests (Rate limit exceeded, check Retry-After header)" });

        var problem = context.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), context.SchemaRepository);
        foreach (var (status, response) in operation.Responses)
        {
            if (status[0] is '4' or '5' && response.Content.Count == 0)
            {
                response.Content["application/problem+json"] = new OpenApiMediaType { Schema = problem };
            }
        }
    }
}
