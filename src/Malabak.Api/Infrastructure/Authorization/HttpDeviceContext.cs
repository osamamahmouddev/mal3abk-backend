using Malabak.Modules.Identity.Application.Sessions;

namespace Malabak.Api.Infrastructure.Authorization;

/// <summary>Device details from the current HTTP request. Clients send X-Device-Id (stable install id) and X-Device-Name.</summary>
public sealed class HttpDeviceContext(IHttpContextAccessor accessor) : IDeviceContext
{
    public const string DeviceIdHeader = "X-Device-Id";
    public const string DeviceNameHeader = "X-Device-Name";

    public string? DeviceId => Header(DeviceIdHeader);
    public string? DeviceName => Header(DeviceNameHeader);
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? UserAgent => Header("User-Agent");

    private string? Header(string name)
    {
        var value = accessor.HttpContext?.Request.Headers[name].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
