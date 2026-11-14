using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Malabak.Api.Infrastructure.Configuration;
using Malabak.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Malabak.UnitTests;

public sealed class GlobalRateLimitPartitionerTests
{
    private static readonly RateLimiterOptions _options = new() { PermitLimit = 100, WindowSeconds = 60, QueueLimit = 10 };

    private static DefaultHttpContext Context(string ip, string? userId = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (userId is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId)], authenticationType: "Bearer"));
        }

        return context;
    }

    [Fact]
    public void GetPartition_SignedInCallersBehindOneIp_GetSeparateBuckets()
    {
        var first = GlobalRateLimitPartitioner.GetPartition(Context("41.0.0.1", "user-a"), _options);
        var second = GlobalRateLimitPartitioner.GetPartition(Context("41.0.0.1", "user-b"), _options);

        first.PartitionKey.Should().Be("user:user-a");
        second.PartitionKey.Should().NotBe(first.PartitionKey);
    }

    [Fact]
    public void GetPartition_AnonymousCaller_IsBucketedByIp()
    {
        var partition = GlobalRateLimitPartitioner.GetPartition(Context("41.0.0.1"), _options);

        partition.PartitionKey.Should().Be("ip:41.0.0.1");
    }

    [Fact]
    public void GetPartition_UnauthenticatedIdentityWithSubClaim_IsStillBucketedByIp()
    {
        var context = Context("41.0.0.2");
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "forged")]));

        GlobalRateLimitPartitioner.GetPartition(context, _options).PartitionKey.Should().Be("ip:41.0.0.2");
    }

    [Fact]
    public void GetPartition_UsesConfiguredNumbers()
    {
        var options = new RateLimiterOptions { PermitLimit = 2, WindowSeconds = 60, QueueLimit = 0 };
        var partition = GlobalRateLimitPartitioner.GetPartition(Context("41.0.0.3", "user-c"), options);
        using var limiter = partition.Factory(partition.PartitionKey);

        limiter.AttemptAcquire().IsAcquired.Should().BeTrue();
        limiter.AttemptAcquire().IsAcquired.Should().BeTrue();
        limiter.AttemptAcquire().IsAcquired.Should().BeFalse();
    }
}
