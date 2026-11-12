using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class EndpointInventoryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _output;

    public EndpointInventoryTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public sealed record EndpointInfo(
        string Method,
        string Route,
        string AuthPolicy,
        string RateLimitPolicy,
        bool AllowAnonymous,
        string DisplayName);

    public static List<EndpointInfo> ExtractEndpoints(IServiceProvider services)
    {
        var endpointDataSources = services.GetServices<EndpointDataSource>();
        var endpoints = endpointDataSources.SelectMany(ds => ds.Endpoints).ToList();

        var list = new List<EndpointInfo>();

        foreach (var endpoint in endpoints)
        {
            if (endpoint is not RouteEndpoint routeEndpoint)
            {
                continue;
            }

            var route = "/" + (routeEndpoint.RoutePattern.RawText?.TrimStart('/') ?? string.Empty);

            var httpMethodMetadata = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>();
            var methods = httpMethodMetadata?.HttpMethods?.ToList() ?? new List<string> { "ANY" };

            var authorizeData = endpoint.Metadata.OfType<IAuthorizeData>().ToList();
            var allowAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null;

            string authPolicy;
            if (allowAnonymous)
            {
                authPolicy = "AllowAnonymous";
            }
            else if (authorizeData.Count > 0)
            {
                var policies = authorizeData.Select(a =>
                {
                    if (!string.IsNullOrWhiteSpace(a.Policy)) return a.Policy;
                    if (!string.IsNullOrWhiteSpace(a.Roles)) return $"Roles: {a.Roles}";
                    return "RequiresAuthorization";
                }).Distinct();
                authPolicy = string.Join(", ", policies);
            }
            else
            {
                authPolicy = "None (Implicit)";
            }

            var rateLimitAttr = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();
            var disableRateLimit = endpoint.Metadata.GetMetadata<DisableRateLimitingAttribute>() != null;
            string rateLimitPolicy;
            if (disableRateLimit)
            {
                rateLimitPolicy = "Disabled";
            }
            else if (rateLimitAttr != null)
            {
                rateLimitPolicy = rateLimitAttr.PolicyName ?? "Default";
            }
            else
            {
                rateLimitPolicy = "None";
            }

            foreach (var method in methods)
            {
                list.Add(new EndpointInfo(
                    Method: method,
                    Route: route,
                    AuthPolicy: authPolicy,
                    RateLimitPolicy: rateLimitPolicy,
                    AllowAnonymous: allowAnonymous,
                    DisplayName: endpoint.DisplayName ?? route
                ));
            }
        }

        return list.OrderBy(e => e.Route).ThenBy(e => e.Method).ToList();
    }

    [Fact]
    public void EndpointInventory_ShouldEnumerateAllEndpointsWithoutWritingToDisk()
    {
        // Force server creation
        _ = _factory.CreateClient();

        var endpoints = ExtractEndpoints(_factory.Services);
        _output.WriteLine($"Total endpoints found: {endpoints.Count}");

        endpoints.Should().NotBeEmpty();
        endpoints.Count.Should().BeGreaterThan(200);

        // Only write to disk when explicitly invoked via scripts/generate-endpoint-inventory.sh
        if (Environment.GetEnvironmentVariable("GENERATE_ENDPOINT_INVENTORY") == "1")
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Malabak API — Endpoint Inventory");
            sb.AppendLine();
            sb.AppendLine($"> Generated automatically from ASP.NET Core `EndpointDataSource`. Total routes: {endpoints.Count}");
            sb.AppendLine();
            sb.AppendLine("| Method | Route | Auth Policy | Rate Limit Policy | Allow Anonymous |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");

            foreach (var ep in endpoints)
            {
                sb.AppendLine($"| `{ep.Method}` | `{ep.Route}` | {ep.AuthPolicy} | {ep.RateLimitPolicy} | {(ep.AllowAnonymous ? "✅ Yes" : "❌ No")} |");
            }

            var inventoryPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../docs/testing/endpoint-inventory.md"));
            Directory.CreateDirectory(Path.GetDirectoryName(inventoryPath)!);
            File.WriteAllText(inventoryPath, sb.ToString());

            _output.WriteLine($"Inventory written to: {inventoryPath}");
        }
    }
}
