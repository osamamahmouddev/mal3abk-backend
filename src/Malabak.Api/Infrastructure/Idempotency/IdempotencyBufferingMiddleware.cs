using Microsoft.AspNetCore.Http;

namespace Malabak.Api.Infrastructure.Idempotency;

public sealed class IdempotencyBufferingMiddleware
{
    private const string _idempotencyKeyHeader = "Idempotency-Key";
    private readonly RequestDelegate _next;

    public IdempotencyBufferingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.ContainsKey(_idempotencyKeyHeader))
        {
            context.Request.EnableBuffering();
        }

        await _next(context);
    }
}
