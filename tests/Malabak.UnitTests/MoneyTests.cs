using FluentAssertions;
using Malabak.SharedKernel;
using Xunit;

namespace Malabak.UnitTests;

public sealed class MoneyTests
{
    [Fact]
    public void Constructor_WithValidAmount_SetsPropertiesCorrectly()
    {
        // Arrange & Act
        var money = new Money(150.50m, "EGP");

        // Assert
        money.Amount.Should().Be(150.50m);
        money.Currency.Should().Be("EGP");
    }

    [Fact]
    public void Constructor_RoundsToTwoDecimalPlaces_AwayFromZero()
    {
        // Act
        var m1 = new Money(100.555m);
        var m2 = new Money(100.554m);

        // Assert
        m1.Amount.Should().Be(100.56m);
        m2.Amount.Should().Be(100.55m);
    }

    [Fact]
    public void Constructor_WithNegativeAmount_ThrowsArgumentException()
    {
        // Act
        var act = () => new Money(-10m);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Amount cannot be negative*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("EG")]
    [InlineData("EGPP")]
    [InlineData("123")]
    public void Constructor_WithInvalidCurrency_ThrowsArgumentException(string currency)
    {
        // Act
        var act = () => new Money(100m, currency);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithValidAmount_ReturnsSuccess()
    {
        // Act
        var result = Money.Create(250.75m, "egp");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(250.75m);
        result.Value.Currency.Should().Be("EGP");
    }

    [Fact]
    public void Create_WithNegativeAmount_ReturnsFailure()
    {
        // Act
        var result = Money.Create(-50m);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MoneyErrors.NegativeAmount);
    }

    [Fact]
    public void Create_WithInvalidCurrency_ReturnsFailure()
    {
        // Act
        var result = Money.Create(100m, "INVALID");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MoneyErrors.InvalidCurrency);
    }

    [Fact]
    public void OperatorPlus_WithSameCurrency_ReturnsCorrectSum()
    {
        // Arrange
        var m1 = new Money(100m, "EGP");
        var m2 = new Money(50m, "EGP");

        // Act
        var result = m1 + m2;

        // Assert
        result.Amount.Should().Be(150m);
        result.Currency.Should().Be("EGP");
    }

    [Fact]
    public void OperatorPlus_WithDifferentCurrencies_ThrowsInvalidOperationException()
    {
        // Arrange
        var m1 = new Money(100m, "EGP");
        var m2 = new Money(50m, "USD");

        // Act
        var act = () => _ = m1 + m2;

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*different currencies*");
    }

    [Fact]
    public void OperatorMinus_WithSameCurrency_ReturnsCorrectDifference()
    {
        // Arrange
        var m1 = new Money(100m, "EGP");
        var m2 = new Money(40m, "EGP");

        // Act
        var result = m1 - m2;

        // Assert
        result.Amount.Should().Be(60m);
    }

    [Fact]
    public void OperatorMinus_ResultingInNegative_ThrowsInvalidOperationException()
    {
        // Arrange
        var m1 = new Money(50m, "EGP");
        var m2 = new Money(100m, "EGP");

        // Act
        var act = () => _ = m1 - m2;

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be negative*");
    }

    [Fact]
    public void OperatorMultiply_WithDecimalMultiplier_ReturnsScaledAmount()
    {
        // Arrange
        var money = new Money(100m, "EGP");

        // Act
        var result = money * 1.5m;

        // Assert
        result.Amount.Should().Be(150m);
        result.Currency.Should().Be("EGP");
    }

    [Fact]
    public void OperatorMultiply_WithNegativeMultiplier_ThrowsArgumentException()
    {
        // Arrange
        var money = new Money(100m, "EGP");

        // Act
        var act = () => _ = money * -2m;

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be negative*");
    }

    [Fact]
    public void OperatorDivide_WithValidDivisor_ReturnsDividedAmount()
    {
        // Arrange
        var money = new Money(300m, "EGP");

        // Act
        var result = money / 3;

        // Assert
        result.Amount.Should().Be(100m);
    }

    [Fact]
    public void OperatorDivide_WithZeroOrNegativeDivisor_ThrowsArgumentException()
    {
        // Arrange
        var money = new Money(100m, "EGP");

        // Act
        var actZero = () => _ = money / 0;
        var actNegative = () => _ = money / -2;

        // Assert
        actZero.Should().Throw<ArgumentException>();
        actNegative.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComparisonOperators_WorkCorrectly()
    {
        // Arrange
        var small = new Money(50m, "EGP");
        var large = new Money(150m, "EGP");
        var equal = new Money(50m, "EGP");

        // Assert
        (small < large).Should().BeTrue();
        (large > small).Should().BeTrue();
        (small <= equal).Should().BeTrue();
        (small >= equal).Should().BeTrue();
        (small == equal).Should().BeTrue();
        small.CompareTo(large).Should().BeNegative();
        large.CompareTo(small).Should().BePositive();
        small.CompareTo(equal).Should().Be(0);
    }

    [Fact]
    public void ToString_FormatsWithTwoDecimalsAndCurrency()
    {
        // Arrange
        var money = new Money(1500.5m, "EGP");

        // Act
        var str = money.ToString();

        // Assert
        str.Should().Be("1,500.50 EGP");
    }
}
