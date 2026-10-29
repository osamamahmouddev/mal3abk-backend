using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Malabak.Api.Infrastructure.Security;

public sealed class CloudflareRealIpMiddleware
{
    private const string _cfConnectingIpHeader = "CF-Connecting-IP";
    private readonly RequestDelegate _next;
    private readonly ILogger<CloudflareRealIpMiddleware> _logger;
    private readonly CloudflareOptions _options;
    private readonly bool _isDevelopment;
    private readonly List<IPNetwork> _trustedNetworks;

    private static readonly string[] _cloudflareCidrs =
    [
        // IPv4
        "173.245.48.0/20",
        "103.21.244.0/22",
        "103.22.200.0/22",
        "103.31.4.0/22",
        "141.101.64.0/18",
        "108.162.192.0/18",
        "190.93.240.0/20",
        "188.114.96.0/20",
        "197.234.240.0/22",
        "198.41.128.0/17",
        "162.158.0.0/15",
        "104.16.0.0/13",
        "104.24.0.0/14",
        "172.64.0.0/13",
        "131.0.72.0/22",
        // IPv6
        "2400:cb00::/32",
        "2606:4700::/32",
        "2803:f800::/32",
        "2405:b500::/32",
        "2405:8100::/32",
        "2a06:98c0::/29",
        "2c0f:f248::/32"
    ];

    public CloudflareRealIpMiddleware(
        RequestDelegate next,
        ILogger<CloudflareRealIpMiddleware> logger,
        IOptions<CloudflareOptions> options,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
        _isDevelopment = environment.IsDevelopment();

        _trustedNetworks = [];
        foreach (var cidr in _cloudflareCidrs)
        {
            if (IPNetwork.TryParse(cidr, out var network))
            {
                _trustedNetworks.Add(network);
            }
        }

        foreach (var proxy in _options.AdditionalTrustedProxies)
        {
            if (IPNetwork.TryParse(proxy, out var network))
            {
                _trustedNetworks.Add(network);
            }
            else if (IPAddress.TryParse(proxy, out var ip))
            {
                _trustedNetworks.Add(new IPNetwork(ip, ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128));
            }
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_options.Enabled)
        {
            ProcessRealIp(context);
        }

        await _next(context);
    }

    private void ProcessRealIp(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null)
        {
            if (_isDevelopment && _options.TrustLoopbackInDevelopment)
            {
                remoteIp = IPAddress.Loopback;
                context.Connection.RemoteIpAddress = remoteIp;
            }
            else
            {
                return;
            }
        }

        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        var isTrusted = IsTrustedProxy(remoteIp);

        if (context.Request.Headers.TryGetValue(_cfConnectingIpHeader, out var cfHeader) && cfHeader.Count > 0)
        {
            var candidateIpStr = cfHeader.ToString().Trim();
            if (isTrusted && IPAddress.TryParse(candidateIpStr, out var realClientIp))
            {
                context.Connection.RemoteIpAddress = realClientIp;
                context.Items["CF-Real-IP"] = realClientIp;
            }
            else if (!isTrusted)
            {
                _logger.LogWarning(
                    "Untrusted proxy {RemoteIp} attempted to supply {Header}: {SpoofedIp}. Header will be ignored.",
                    remoteIp,
                    _cfConnectingIpHeader,
                    candidateIpStr);
            }
        }
    }

    private bool IsTrustedProxy(IPAddress ip)
    {
        if (_isDevelopment && _options.TrustLoopbackInDevelopment && IPAddress.IsLoopback(ip))
        {
            return true;
        }

        return _trustedNetworks.Any(network => network.Contains(ip));
    }
}
