using FluentAssertions;
using Malabak.SharedKernel.Caching;
using Xunit;

namespace Malabak.UnitTests;

public sealed class RedisKeyConventionsTests
{
    private readonly RedisKeyConventions _builder = new("development", "malabak");

    [Fact]
    public void Constructor_WithValidArguments_ShouldSetProperties()
    {
        // Act
        var builder = new RedisKeyConventions("Production", "Malabak");

        // Assert
        builder.Environment.Should().Be("production");
        builder.Prefix.Should().Be("malabak");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithNullOrWhitespaceEnvironment_ShouldThrowArgumentException(string? invalidEnv)
    {
        // Act
        var act = () => new RedisKeyConventions(invalidEnv!, "malabak");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("dev elopment")]
    [InlineData("dev\nprod")]
    [InlineData("dev\tstage")]
    public void Constructor_WithInvalidCharsInEnvironment_ShouldThrowArgumentException(string invalidEnv)
    {
        // Act
        var act = () => new RedisKeyConventions(invalidEnv, "malabak");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*contains illegal whitespace characters*");
    }

    [Fact]
    public void Otp_WithValidArguments_ShouldFormatCorrectKey()
    {
        // Act
        var key = _builder.Otp("+201012345678", "login");

        // Assert
        key.Should().Be("malabak:development:identity:otp:login:+201012345678");
    }

    [Fact]
    public void WsTicket_WithValidJti_ShouldFormatCorrectKey()
    {
        // Act
        var key = _builder.WsTicket("ticket-uuid-1234");

        // Assert
        key.Should().Be("malabak:development:identity:ws-ticket:ticket-uuid-1234");
    }

    [Fact]
    public void Impersonation_WithValidCode_ShouldFormatCorrectKey()
    {
        // Act
        var key = _builder.Impersonation("code-abc-987");

        // Assert
        key.Should().Be("malabak:development:identity:impersonation:code-abc-987");
    }

    [Fact]
    public void SlotLock_WithValidPitchAndTimestamp_ShouldFormatNormalizedKey()
    {
        // Arrange
        var pitchId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var slotUtc = new DateTime(2026, 10, 28, 18, 30, 0, DateTimeKind.Utc);

        // Act
        var key = _builder.SlotLock(pitchId, slotUtc);

        // Assert
        key.Should().Be("malabak:development:booking:lock:slot:11111111222233334444555555555555:202610281830");
    }

    [Fact]
    public void SlotLock_WithEmptyPitchId_ShouldThrowArgumentException()
    {
        // Act
        var act = () => _builder.SlotLock(Guid.Empty, DateTime.UtcNow);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*PitchId cannot be empty*");
    }

    [Fact]
    public void RateLimit_WithCategoryAndIdentifier_ShouldFormatCorrectKey()
    {
        // Act
        var key = _builder.RateLimit("ip", "192.168.1.1");

        // Assert
        key.Should().Be("malabak:development:ratelimit:ip:192.168.1.1");
    }

    [Fact]
    public void Format_WithCustomModuleAndIdentifiers_ShouldConstructKey()
    {
        // Act
        var key = _builder.Format("venues", "search", "cairo", "5aside");

        // Assert
        key.Should().Be("malabak:development:venues:search:cairo:5aside");
    }

    [Fact]
    public void BuildKey_WithEmptyIdentifiers_ShouldThrowArgumentException()
    {
        // Act
        var act = () => RedisKeyConventions.BuildKey("malabak", "prod", "venues", "search");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*At least one identifier must be provided*");
    }

    [Theory]
    [InlineData("identity", "otp", "   ")]
    [InlineData("identity", "otp", "invalid segment with space")]
    public void Format_WithInvalidIdentifier_ShouldThrowArgumentException(string module, string category, string invalidId)
    {
        // Act
        var act = () => _builder.Format(module, category, invalidId);

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
