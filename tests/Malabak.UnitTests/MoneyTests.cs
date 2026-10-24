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
    public void Constructor_WithNegativeAmount_ThrowsArgumentException()
    {
        // Act
        var act = () => new Money(-10m);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Amount cannot be negative*");
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
    public void OperatorMinus_WithDifferentCurrencies_ThrowsInvalidOperationException()
    {
        // Arrange
        var m1 = new Money(100m, "EGP");
        var m2 = new Money(50m, "USD");

        // Act
        var act = () => _ = m1 - m2;

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*different currencies*");
    }
}
