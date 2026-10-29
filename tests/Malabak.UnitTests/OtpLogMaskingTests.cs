using System.Reflection;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Authentication.SendOtp;
using Malabak.Modules.Identity.Application.Authentication.VerifyOtp;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>SECURITY.md §15.5: the OTP flow logs user ids or masked numbers, never a full phone number.</summary>
public sealed class OtpLogMaskingTests
{
    private const string _rawPhone = "01012345678";
    private const string _normalizedPhone = "+201012345678";
    private const string _maskedPhone = "+2010•••••678";

    private readonly IPhoneNumberNormalizer _normalizer = new PhoneNumberNormalizer();
    private readonly IOtpSecurityService _securityService;
    private readonly IConfiguration _configuration;

    public OtpLogMaskingTests()
    {
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Otp:HmacSecret"] = "TestSecretKeyForHmacSha256Authentication123456!",
                ["Security:Turnstile:BypassInDevelopment"] = "true",
            })
            .Build();
        _securityService = new OtpSecurityService(_configuration);
    }

    [Fact]
    public async Task VerifyOtpHandler_WhenMaxAttemptsExceeded_LogsMaskedNumberOnlyAsync()
    {
        var store = new InMemoryOtpStore();
        await store.SaveOtpAsync(_normalizedPhone, _securityService.ComputeHmacHash("123456"), TimeSpan.FromMinutes(5), maxVerificationAttempts: 1);
        var logger = new CapturingLogger<VerifyOtpHandler>();
        await using var db = CreateDbContext();
        var handler = new VerifyOtpHandler(_normalizer, _securityService, store, db, new FakeClock(), logger);

        var result = await handler.HandleAsync(new VerifyOtpCommand(_rawPhone, "000000"));

        result.Error.Code.Should().Be("Identity.OtpMaxAttemptsExceeded");
        logger.AllText.Should().Contain(_maskedPhone);
        AssertNoFullNumber(logger.AllText);
    }

    [Fact]
    public async Task VerifyOtpHandler_WhenAutoRegistering_LogsUserIdNotNumberAsync()
    {
        var store = new InMemoryOtpStore();
        await store.SaveOtpAsync(_normalizedPhone, _securityService.ComputeHmacHash("123456"), TimeSpan.FromMinutes(5));
        var logger = new CapturingLogger<VerifyOtpHandler>();
        await using var db = CreateDbContext();
        var handler = new VerifyOtpHandler(_normalizer, _securityService, store, db, new FakeClock(), logger);

        var result = await handler.HandleAsync(new VerifyOtpCommand(_rawPhone, "123456"));

        result.IsSuccess.Should().BeTrue();
        result.Value.IsNewUser.Should().BeTrue();
        logger.AllText.Should().Contain(result.Value.UserId.ToString());
        AssertNoFullNumber(logger.AllText);
    }

    [Fact]
    public async Task SendOtpHandler_LogsNoFullNumber_OnSuccessOrRateLimitAsync()
    {
        var store = new InMemoryOtpStore();
        var turnstile = new TurnstileValidator(_configuration, NullLogger<TurnstileValidator>.Instance);
        var logger = new CapturingLogger<SendOtpHandler>();
        var handler = new SendOtpHandler(_normalizer, turnstile, _securityService, store, new AcceptingOtpSender(), logger);
        var command = new SendOtpCommand(_rawPhone, "test-turnstile-token");

        for (var i = 0; i < 4; i++)
        {
            await handler.HandleAsync(command);
        }

        logger.Entries.Should().NotBeEmpty();
        AssertNoFullNumber(logger.AllText);
    }

    [Fact]
    public async Task RedisOtpStore_WhenRateLimitExceeded_LogsMaskedNumberOnlyAsync()
    {
        var logger = new CapturingLogger<RedisOtpStore>();
        var store = new RedisOtpStore(FakeRedis.WithSortedSetLength(3), logger);

        var allowed = await store.CheckAndRecordSendAttemptAsync(_normalizedPhone, maxAttempts: 3);

        allowed.Should().BeFalse();
        logger.AllText.Should().Contain(_maskedPhone);
        AssertNoFullNumber(logger.AllText);
    }

    private static void AssertNoFullNumber(string logText)
    {
        logText.Should().NotContain(_normalizedPhone);
        logText.Should().NotContain(_rawPhone);
        logText.Should().NotContain("1012345678");
    }

    private static IdentityDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class AcceptingOtpSender : IOtpSender
    {
        public Task<bool> SendAsync(string phoneNumber, string code, OtpPurpose purpose, TimeSpan validity, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    /// <summary>
    /// Stands in for Redis: every <see cref="Task{Long}"/>-returning call (sorted-set length and
    /// range removal) yields the configured count, and <see cref="IConnectionMultiplexer.GetDatabase"/>
    /// returns that database.
    /// </summary>
    public class FakeRedis : DispatchProxy
    {
        private long _sortedSetLength;
        private object? _database;

        public static IConnectionMultiplexer WithSortedSetLength(long length)
        {
            var database = Create<IDatabase, FakeRedis>();
            ((FakeRedis)(object)database)._sortedSetLength = length;

            var multiplexer = Create<IConnectionMultiplexer, FakeRedis>();
            ((FakeRedis)(object)multiplexer)._database = database;
            return multiplexer;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod switch
        {
            { Name: nameof(IConnectionMultiplexer.GetDatabase) } => _database,
            { ReturnType: var type } when type == typeof(Task<long>) => Task.FromResult(_sortedSetLength),
            _ => throw new NotSupportedException(targetMethod?.Name),
        };
    }
}
