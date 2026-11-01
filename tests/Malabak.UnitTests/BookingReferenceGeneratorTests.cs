using FluentAssertions;
using Malabak.Modules.Booking.Infrastructure;
using Xunit;

namespace Malabak.UnitTests;

public sealed class BookingReferenceGeneratorTests
{
    private readonly BookingReferenceGenerator _generator = new();

    [Fact]
    public void Generate_ReturnsExpectedFormat_MLBAnd6ValidCharacters()
    {
        // Act
        var reference = _generator.Generate();

        // Assert
        reference.Should().StartWith("MLB-");
        reference.Length.Should().Be(10);
        _generator.IsValid(reference).Should().BeTrue();
    }

    [Fact]
    public void Generate_DoesNotContainAmbiguousCharacters()
    {
        // Act & Assert (across 200 generations)
        for (var i = 0; i < 200; i++)
        {
            var reference = _generator.Generate();
            var code = reference[4..];
            code.Should().NotContainAny("0", "1", "I", "O");
        }
    }

    [Fact]
    public void Generate_MultipleCalls_ProduceUniqueReferences()
    {
        // Arrange
        const int count = 1000;
        var generated = new HashSet<string>(count);

        // Act
        for (var i = 0; i < count; i++)
        {
            generated.Add(_generator.Generate());
        }

        // Assert
        generated.Should().HaveCount(count);
    }

    [Theory]
    [InlineData("MLB-4F7K2Q", true)]
    [InlineData("MLB-234567", true)]
    [InlineData("MLB-ZYXWVU", true)]
    [InlineData("MLB-4F7K2", false)] // too short
    [InlineData("MLB-4F7K2QA", false)] // too long
    [InlineData("BK-4F7K2Q", false)] // wrong prefix
    [InlineData("MLB-4F7K20", false)] // contains '0'
    [InlineData("MLB-4F7K21", false)] // contains '1'
    [InlineData("MLB-4F7K2I", false)] // contains 'I'
    [InlineData("MLB-4F7K2O", false)] // contains 'O'
    [InlineData("MLB-4f7k2q", false)] // lowercase
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_ValidatesCorrectly(string? reference, bool expected)
    {
        _generator.IsValid(reference!).Should().Be(expected);
    }
}
