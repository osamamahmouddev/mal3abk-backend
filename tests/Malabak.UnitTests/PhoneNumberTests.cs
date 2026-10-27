using FluentAssertions;
using Malabak.SharedKernel;
using Xunit;

namespace Malabak.UnitTests;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("01012345678", "+201012345678")]
    [InlineData("01112345678", "+201112345678")]
    [InlineData("01212345678", "+201212345678")]
    [InlineData("01512345678", "+201512345678")]
    [InlineData("+201012345678", "+201012345678")]
    [InlineData("00201012345678", "+201012345678")]
    [InlineData("201012345678", "+201012345678")]
    [InlineData(" 010 1234 5678 ", "+201012345678")]
    [InlineData("010-1234-5678", "+201012345678")]
    [InlineData("(010) 1234-5678", "+201012345678")]
    public void Create_WithValidEgyptianNumbers_NormalizesToE164(string raw, string expected)
    {
        // Act
        var result = PhoneNumber.Create(raw);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expected);
        result.Value.IsEgyptian.Should().BeTrue();
    }

    [Theory]
    [InlineData("01312345678")] // Invalid Egyptian prefix (not 0, 1, 2, 5)
    [InlineData("01412345678")]
    [InlineData("01612345678")]
    [InlineData("0101234567")]  // Too short (10 digits)
    [InlineData("010123456789")] // Too long (12 digits)
    [InlineData("+201312345678")]
    public void Create_WithInvalidEgyptianNumbers_ReturnsFailure(string raw)
    {
        // Act
        var result = PhoneNumber.Create(raw);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PhoneNumberErrors.InvalidEgyptianNumber);
    }

    [Theory]
    [InlineData("+966501234567", "+966501234567")] // Saudi Arabia
    [InlineData("+971501234567", "+971501234567")] // UAE
    [InlineData("+14155552671", "+14155552671")]   // USA
    [InlineData("+447911123456", "+447911123456")]  // UK
    public void Create_WithValidInternationalNumbers_NormalizesSuccessfully(string raw, string expected)
    {
        // Act
        var result = PhoneNumber.Create(raw);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expected);
        result.Value.IsEgyptian.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyOrNull_ReturnsEmptyError(string? raw)
    {
        // Act
        var result = PhoneNumber.Create(raw);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PhoneNumberErrors.Empty);
    }

    [Theory]
    [InlineData("+12")] // Too short
    [InlineData("abcdefghijk")]
    [InlineData("+12345678901234567")] // Too long
    public void Create_WithInvalidInternationalFormat_ReturnsInvalidFormatError(string raw)
    {
        // Act
        var result = PhoneNumber.Create(raw);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PhoneNumberErrors.InvalidFormat);
    }

    [Fact]
    public void ToMaskedString_ForEgyptianNumber_MasksMiddleDigits()
    {
        // Arrange
        var phone = PhoneNumber.From("01012345678");

        // Act
        var masked = phone.ToMaskedString();

        // Assert
        masked.Should().Be("+2010****5678");
    }

    [Fact]
    public void ToMaskedString_ForInternationalNumber_MasksMiddleDigits()
    {
        // Arrange
        var phone = PhoneNumber.From("+966501234567");

        // Act
        var masked = phone.ToMaskedString();

        // Assert
        masked.Should().Be("+966*****4567");
    }

    [Fact]
    public void ImplicitOperator_ConvertsToString()
    {
        // Arrange
        var phone = PhoneNumber.From("01012345678");

        // Act
        string str = phone;

        // Assert
        str.Should().Be("+201012345678");
    }
}
