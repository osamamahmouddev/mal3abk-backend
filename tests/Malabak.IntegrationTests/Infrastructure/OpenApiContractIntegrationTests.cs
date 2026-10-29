using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class OpenApiContractIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OpenApiContractIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SwaggerEndpoint_ReturnsValidOpenApiDocumentWithStandardErrorsAsync()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/swagger/v1/swagger.json");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrWhiteSpace();

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        // Verify OpenAPI version and basic info
        root.TryGetProperty("openapi", out var openapiProp).Should().BeTrue();
        openapiProp.GetString().Should().StartWith("3.");

        var info = root.GetProperty("info");
        info.GetProperty("title").GetString().Should().Be("Malabak API");
        info.GetProperty("version").GetString().Should().Be("v1");

        // Verify Security Scheme (Bearer ES256 JWT)
        var components = root.GetProperty("components");
        var securitySchemes = components.GetProperty("securitySchemes");
        securitySchemes.TryGetProperty("Bearer", out var bearerScheme).Should().BeTrue();
        bearerScheme.GetProperty("type").GetString().Should().Be("http");
        bearerScheme.GetProperty("scheme").GetString().Should().BeEquivalentTo("bearer");
        bearerScheme.GetProperty("bearerFormat").GetString().Should().Be("JWT");

        // Verify standard error codes documented on endpoints
        var paths = root.GetProperty("paths");
        paths.EnumerateObject().Should().NotBeEmpty();

        var firstPath = paths.EnumerateObject().First().Value;
        var firstOperation = firstPath.EnumerateObject().First().Value;
        var responses = firstOperation.GetProperty("responses");

        // Standard error codes: 400, 401, 403, 404, 409, 422, 429
        responses.TryGetProperty("400", out _).Should().BeTrue();
        responses.TryGetProperty("401", out _).Should().BeTrue();
        responses.TryGetProperty("403", out _).Should().BeTrue();
        responses.TryGetProperty("404", out _).Should().BeTrue();
        responses.TryGetProperty("409", out _).Should().BeTrue();
        responses.TryGetProperty("422", out _).Should().BeTrue();
        responses.TryGetProperty("429", out _).Should().BeTrue();

        // Save/Sync to docs/openapi/v1.json
        var repoRoot = FindRepoRoot();
        if (repoRoot != null)
        {
            var openApiDir = Path.Combine(repoRoot, "docs", "openapi");
            Directory.CreateDirectory(openApiDir);
            var targetPath = Path.Combine(openApiDir, "v1.json");
            var formattedJson = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(targetPath, formattedJson);
        }
    }

    private static string? FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null && !File.Exists(Path.Combine(current.FullName, "Malabak.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName;
    }
}
