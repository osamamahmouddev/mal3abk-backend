using System.Text.Json;
using Malabak.SharedKernel.Contracts;

namespace Malabak.Api.Infrastructure.Audit;

/// <summary>Request facts for audit entries: client IP (already resolved behind Cloudflare), impersonator and trace id.</summary>
public sealed class HttpAuditRequestContext(IHttpContextAccessor accessor) : IAuditRequestContext
{
    /// <summary>RFC 8693 actor claim carried by impersonation tokens: {"sub":"&lt;admin id&gt;"}.</summary>
    public const string ActorClaim = "act";

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public Guid? ImpersonatorId => ParseActor(accessor.HttpContext?.User.FindFirst(ActorClaim)?.Value);

    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;

    public static Guid? ParseActor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Guid.TryParse(value, out var direct))
        {
            return direct;
        }

        try
        {
            using var doc = JsonDocument.Parse(value);
            return doc.RootElement.TryGetProperty("sub", out var sub) && Guid.TryParse(sub.GetString(), out var id) ? id : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
