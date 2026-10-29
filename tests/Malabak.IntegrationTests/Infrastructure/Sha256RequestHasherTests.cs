using System.Text;
using FluentAssertions;
using Malabak.Api.Infrastructure.Idempotency;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class Sha256RequestHasherTests
{
    private readonly Sha256RequestHasher _hasher = new();

    [Fact]
    public async Task ComputeHashAsync_WithSameRequestAttributes_ShouldReturnSameHashAsync()
    {
        // Arrange
        var context1 = new DefaultHttpContext();
        context1.Request.Method = "POST";
        context1.Request.Path = "/api/v1/bookings";
        context1.Request.QueryString = new QueryString("?promo=SAVE10");
        var bodyBytes1 = Encoding.UTF8.GetBytes("{\"pitchId\":\"111\",\"slot\":\"18:00\"}");
        context1.Request.Body = new MemoryStream(bodyBytes1);

        var context2 = new DefaultHttpContext();
        context2.Request.Method = "POST";
        context2.Request.Path = "/api/v1/bookings";
        context2.Request.QueryString = new QueryString("?promo=SAVE10");
        var bodyBytes2 = Encoding.UTF8.GetBytes("{\"pitchId\":\"111\",\"slot\":\"18:00\"}");
        context2.Request.Body = new MemoryStream(bodyBytes2);

        // Act
        var hash1 = await _hasher.ComputeHashAsync(context1.Request);
        var hash2 = await _hasher.ComputeHashAsync(context2.Request);

        // Assert
        hash1.Should().NotBeNullOrWhiteSpace();
        hash1.Length.Should().Be(64); // SHA-256 hex string length
        hash1.Should().Be(hash2);

        // Verify stream position was reset to 0
        context1.Request.Body.Position.Should().Be(0);
        context2.Request.Body.Position.Should().Be(0);
    }

    [Fact]
    public async Task ComputeHashAsync_WithDifferentBody_ShouldReturnDifferentHashAsync()
    {
        // Arrange
        var context1 = new DefaultHttpContext();
        context1.Request.Method = "POST";
        context1.Request.Path = "/api/v1/bookings";
        context1.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"pitchId\":\"111\",\"slot\":\"18:00\"}"));

        var context2 = new DefaultHttpContext();
        context2.Request.Method = "POST";
        context2.Request.Path = "/api/v1/bookings";
        context2.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"pitchId\":\"222\",\"slot\":\"20:00\"}"));

        // Act
        var hash1 = await _hasher.ComputeHashAsync(context1.Request);
        var hash2 = await _hasher.ComputeHashAsync(context2.Request);

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public async Task ComputeHashAsync_WithDifferentMethodOrPath_ShouldReturnDifferentHashAsync()
    {
        // Arrange
        var context1 = new DefaultHttpContext();
        context1.Request.Method = "POST";
        context1.Request.Path = "/api/v1/bookings";
        context1.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

        var context2 = new DefaultHttpContext();
        context2.Request.Method = "PUT";
        context2.Request.Path = "/api/v1/bookings";
        context2.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

        var context3 = new DefaultHttpContext();
        context3.Request.Method = "POST";
        context3.Request.Path = "/api/v1/venues";
        context3.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

        // Act
        var hash1 = await _hasher.ComputeHashAsync(context1.Request);
        var hash2 = await _hasher.ComputeHashAsync(context2.Request);
        var hash3 = await _hasher.ComputeHashAsync(context3.Request);

        // Assert
        hash1.Should().NotBe(hash2);
        hash1.Should().NotBe(hash3);
    }
}
