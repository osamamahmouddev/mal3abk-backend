using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Malabak.IntegrationTests;

public sealed class DeepLinkEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DeepLinkEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AndroidAssetLinks_IsPubliclyReachableAndNamesTheAppAsync()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/.well-known/assetlinks.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("com.malabak.app");
    }

    [Fact]
    public async Task AppleSiteAssociation_IsPubliclyReachableAndNamesTheAppAsync()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/.well-known/apple-app-site-association");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("com.malabak.app");
        body.Should().Contain("/join/*");
    }

    [Fact]
    public async Task JoinPage_IsPubliclyReachableAndRedirectsToTheAppAsync()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/join/ABTL01");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("malabak://app/join/ABTL01");
    }
}
