using System.Text.Json;
using FluentAssertions;
using Malabak.Api.Infrastructure.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_InProduction_HidesInternalExceptionDetailsAsync()
    {
        // Arrange
        var logger = NullLogger<GlobalExceptionHandler>.Instance;
        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };

        var handler = new GlobalExceptionHandler(logger, env);

        var httpContext = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;
        httpContext.Request.Path = "/api/v1/test-error";

        var exception = new InvalidOperationException("Fatal database password leak");

        // Act
        var result = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        httpContext.Response.ContentType.Should().StartWith("application/problem+json");

        responseBody.Seek(0, SeekOrigin.Begin);
        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(responseBody);

        problemDetails.Should().NotBeNull();
        problemDetails!.Status.Should().Be(StatusCodes.Status500InternalServerError);
        problemDetails.Title.Should().Be("Internal Server Error");
        problemDetails.Detail.Should().Be("An unexpected error occurred. Please try again later or contact support.");
        problemDetails.Instance.Should().Be("/api/v1/test-error");
        problemDetails.Extensions.Should().ContainKey("traceId");
        problemDetails.Extensions.Should().NotContainKey("stackTrace");
    }

    [Fact]
    public async Task TryHandleAsync_InDevelopment_IncludesExceptionDetailsAndStackTraceAsync()
    {
        // Arrange
        var logger = NullLogger<GlobalExceptionHandler>.Instance;
        var env = new TestHostEnvironment { EnvironmentName = Environments.Development };

        var handler = new GlobalExceptionHandler(logger, env);

        var httpContext = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;
        httpContext.Request.Path = "/api/v1/debug";

        var exception = new InvalidOperationException("Specific developer message");

        // Act
        var result = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

        responseBody.Seek(0, SeekOrigin.Begin);
        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(responseBody);

        problemDetails.Should().NotBeNull();
        problemDetails!.Detail.Should().Contain("Specific developer message");
        problemDetails.Extensions.Should().ContainKey("exception");
        problemDetails.Extensions.Should().ContainKey("stackTrace");
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Malabak.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
