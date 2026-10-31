using FluentAssertions;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Xunit;

namespace Malabak.UnitTests;

public sealed class BookingReferenceTests
{
    [Fact]
    public void Generate_ProducesThePublicFormat()
    {
        var reference = BookingReference.Generate();

        reference.Should().StartWith("MLB-").And.HaveLength(10);
        BookingReference.IsValid(reference).Should().BeTrue();
    }

    [Fact]
    public void Generate_NeverUsesLookAlikeCharacters()
    {
        var codes = string.Concat(Enumerable.Range(0, 5_000).Select(_ => BookingReference.Generate()[BookingReference.Prefix.Length..]));

        codes.Should().NotContainAny("0", "O", "1", "I", "L", "U");
        codes.Distinct().Should().BeEquivalentTo(BookingReference.Alphabet.ToCharArray(), "with this many draws every symbol shows up");
    }

    [Fact]
    public void Generate_IsUniformlyDistributed()
    {
        // 100,000 codes of 6 symbols over 30 symbols: each symbol is expected 20,000 times (standard deviation about
        // 140), so a 5% tolerance is a 7-sigma bound, loose enough never to flake and tight enough to catch modulo bias.
        const int draws = 100_000;
        var counts = BookingReference.Alphabet.ToDictionary(c => c, _ => 0);

        for (var i = 0; i < draws; i++)
        {
            foreach (var c in BookingReference.Generate().AsSpan(BookingReference.Prefix.Length))
            {
                counts[c]++;
            }
        }

        var expected = draws * BookingReference.CodeLength / (double)BookingReference.Alphabet.Length;
        counts.Values.Should().OnlyContain(count => Math.Abs(count - expected) < expected * 0.05);
    }

    [Fact]
    public void Generate_DoesNotRepeatInARow()
    {
        BookingReference.Generate().Should().NotBe(BookingReference.Generate());
    }

    [Theory]
    [InlineData("MLB-4F7K2Q", true)]
    [InlineData("MLB-222222", true)]
    [InlineData("MLB-ZZZZZZ", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("MLB-4F7K2", false)]
    [InlineData("MLB-4F7K2QX", false)]
    [InlineData("mlb-4f7k2q", false)]
    [InlineData("XYZ-4F7K2Q", false)]
    [InlineData("MLB-4F7K20", false)]
    [InlineData("MLB-4F7KIQ", false)]
    [InlineData("MLB-4F7K2 ", false)]
    public void IsValid_AcceptsOnlyGeneratedShapes(string? value, bool expected)
    {
        BookingReference.IsValid(value).Should().Be(expected);
    }

    [Fact]
    public void TheDefaultGenerator_UsesTheBookingReferenceFormat()
    {
        BookingReference.IsValid(new RandomBookingReferenceGenerator().Next()).Should().BeTrue();
    }
}
