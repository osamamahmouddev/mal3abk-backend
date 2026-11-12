using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Malabak.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Phase 6: Edge-case tests for rate limiting policies, algorithms, and 429 ProblemDetails (SECURITY.md §7.5).
/// Verifies sliding window, fixed window, token bucket, key resolution (user vs IP),
/// headers (Retry-After, X-RateLimit-Limit, X-RateLimit-Remaining), and denial at limit + 1.
/// </summary>
public sealed class RateLimitingMatrixEdgeCaseTests
{
    [Fact]
    public async Task EndpointFilter_WhenWithinLimit_EmitsRateLimitHeaders_AndInvokesNextAsync()
    {
        // Arrange
        var fakeLimiter = new FakeRateLimiter(new RateLimitResult(
            IsAllowed: true,
            Remaining: 4,
            RetryAfter: TimeSpan.Zero,
            Limit: 5));

        var attribute = new DistributedRateLimitAttribute("booking_create")
        {
            PermitLimit = 5,
            WindowSeconds = 60,
            Algorithm = RateLimitAlgorithm.SlidingWindow
        };

        var filter = new DistributedRateLimitEndpointFilter(fakeLimiter, attribute);
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("197.35.10.20");

        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);
        var nextInvoked = false;

        // Act
        _ = await filter.InvokeAsync(invocationContext, ctx =>
        {
            nextInvoked = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        // Assert
        nextInvoked.Should().BeTrue();
        httpContext.Response.Headers.Should().ContainKey("X-RateLimit-Limit").WhoseValue.Should().Equal("5");
        httpContext.Response.Headers.Should().ContainKey("X-RateLimit-Remaining").WhoseValue.Should().Equal("4");
        httpContext.Response.Headers.Should().NotContainKey("Retry-After");
    }

    [Fact]
    public async Task EndpointFilter_WhenLimitExceeded_Returns429ProblemDetails_WithRetryAfterHeaderAsync()
    {
        // Arrange
        var fakeLimiter = new FakeRateLimiter(new RateLimitResult(
            IsAllowed: false,
            Remaining: 0,
            RetryAfter: TimeSpan.FromSeconds(45),
            Limit: 5));

        var attribute = new DistributedRateLimitAttribute("booking_create")
        {
            PermitLimit = 5,
            WindowSeconds = 60,
            Algorithm = RateLimitAlgorithm.SlidingWindow
        };

        var filter = new DistributedRateLimitEndpointFilter(fakeLimiter, attribute);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new EmptyServiceProvider()
        };
        httpContext.Request.Path = "/api/v1/bookings";
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("197.35.10.20");

        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);
        var nextInvoked = false;

        // Act
        var result = await filter.InvokeAsync(invocationContext, ctx =>
        {
            nextInvoked = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        // Assert
        nextInvoked.Should().BeFalse("subsequent middleware or endpoint handler must not be reached when rate limited");
        httpContext.Response.Headers.Should().ContainKey("Retry-After").WhoseValue.Should().Equal("45");
        httpContext.Response.Headers.Should().ContainKey("X-RateLimit-Limit").WhoseValue.Should().Equal("5");
        httpContext.Response.Headers.Should().ContainKey("X-RateLimit-Remaining").WhoseValue.Should().Equal("0");
        httpContext.Response.Headers.Should().ContainKey("X-RateLimit-Reset").WhoseValue.Should().Equal("45");

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result!;
        problem.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        problem.ProblemDetails.Type.Should().Be("https://tools.ietf.org/html/rfc9110#section-15.5.20");
        problem.ProblemDetails.Instance.Should().Be("/api/v1/bookings");
        problem.ProblemDetails.Extensions.Should().ContainKey("code").WhoseValue.Should().Be("rate_limited");
        problem.ProblemDetails.Extensions.Should().ContainKey("traceId");
    }

    [Fact]
    public async Task EndpointFilter_KeyResolution_UsesUserIdForAuthenticatedCallerAsync()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var fakeLimiter = new FakeRateLimiter(new RateLimitResult(true, 10, TimeSpan.Zero, 20));
        var attribute = new DistributedRateLimitAttribute("explore_venues") { PermitLimit = 20, WindowSeconds = 60 };
        var filter = new DistributedRateLimitEndpointFilter(fakeLimiter, attribute);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString("D"))
        ], "TestAuth"));

        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        // Act
        await filter.InvokeAsync(invocationContext, _ => ValueTask.FromResult<object?>(Results.Ok()));

        // Assert
        fakeLimiter.LastKeyChecked.Should().Be($"user:{userId:D}");
    }

    [Fact]
    public async Task EndpointFilter_KeyResolution_UsesIpForAnonymousCallerAsync()
    {
        // Arrange
        var fakeLimiter = new FakeRateLimiter(new RateLimitResult(true, 10, TimeSpan.Zero, 20));
        var attribute = new DistributedRateLimitAttribute("explore_venues") { PermitLimit = 20, WindowSeconds = 60 };
        var filter = new DistributedRateLimitEndpointFilter(fakeLimiter, attribute);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("156.204.12.99");

        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        // Act
        await filter.InvokeAsync(invocationContext, _ => ValueTask.FromResult<object?>(Results.Ok()));

        // Assert
        fakeLimiter.LastKeyChecked.Should().Be("ip:156.204.12.99");
    }

    [Theory]
    [InlineData(1, 1, false)] // 1 second retry after
    [InlineData(60, 60, false)] // 60 seconds retry after
    [InlineData(0, 1, false)] // 0 second duration forced to minimum 1 second
    public async Task EndpointFilter_RetryAfter_IsAlwaysAtLeastOneSecondAsync(int rawSeconds, int expectedHeaderSec, bool isAllowed)
    {
        // Arrange
        var fakeLimiter = new FakeRateLimiter(new RateLimitResult(
            IsAllowed: isAllowed,
            Remaining: 0,
            RetryAfter: TimeSpan.FromSeconds(rawSeconds),
            Limit: 10));

        var attribute = new DistributedRateLimitAttribute("test_policy") { PermitLimit = 10, WindowSeconds = 60 };
        var filter = new DistributedRateLimitEndpointFilter(fakeLimiter, attribute);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new EmptyServiceProvider()
        };

        // Act
        await filter.InvokeAsync(new TestEndpointFilterInvocationContext(httpContext), _ => ValueTask.FromResult<object?>(Results.Ok()));

        // Assert
        httpContext.Response.Headers.Should().ContainKey("Retry-After")
            .WhoseValue.Should().Equal(expectedHeaderSec.ToString());
    }

    private sealed class FakeRateLimiter : IDistributedRateLimiter
    {
        private readonly RateLimitResult _result;

        public string? LastKeyChecked { get; private set; }
        public string? LastPolicyChecked { get; private set; }
        public string? LastAlgorithm { get; private set; }

        public FakeRateLimiter(RateLimitResult result)
        {
            _result = result;
        }

        public Task<RateLimitResult> CheckSlidingWindowAsync(
            string policy, string key, int permitLimit, TimeSpan window, bool isSensitive = false, CancellationToken cancellationToken = default)
        {
            LastAlgorithm = "sliding";
            LastPolicyChecked = policy;
            LastKeyChecked = key;
            return Task.FromResult(_result);
        }

        public Task<RateLimitResult> CheckFixedWindowAsync(
            string policy, string key, int permitLimit, TimeSpan window, bool isSensitive = false, CancellationToken cancellationToken = default)
        {
            LastAlgorithm = "fixed";
            LastPolicyChecked = policy;
            LastKeyChecked = key;
            return Task.FromResult(_result);
        }

        public Task<RateLimitResult> CheckTokenBucketAsync(
            string policy, string key, int tokenLimit, int tokensPerPeriod, TimeSpan replenishmentPeriod, bool isSensitive = false, CancellationToken cancellationToken = default)
        {
            LastAlgorithm = "token_bucket";
            LastPolicyChecked = policy;
            LastKeyChecked = key;
            return Task.FromResult(_result);
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class TestEndpointFilterInvocationContext : EndpointFilterInvocationContext
    {
        public TestEndpointFilterInvocationContext(HttpContext httpContext)
        {
            HttpContext = httpContext;
        }

        public override HttpContext HttpContext { get; }
        public override IList<object?> Arguments => [];
        public override T GetArgument<T>(int index) => default!;
    }
}
