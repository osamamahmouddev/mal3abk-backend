using FluentAssertions;
using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel;
using Xunit;

namespace Malabak.UnitTests;

public sealed class VenuesEntityTests
{
    [Fact]
    public void Venue_Create_ShouldDefaultToUnapprovedAndUnpublished()
    {
        // Arrange
        var id = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Act
        var venue = Venue.Create(
            id,
            "Al Ahly Club Pitch",
            "al-ahly-pitch",
            "Premier football pitches",
            ownerId,
            "Nasr City",
            "Cairo",
            "Nasr City Area",
            30.05,
            31.35,
            "+201000000000",
            ["Parking", "Showers"],
            ["https://images.example.com/pitch1.jpg"],
            now);

        // Assert
        venue.Id.Should().Be(id);
        venue.Slug.Should().Be("al-ahly-pitch");
        venue.IsApproved.Should().BeFalse();
        venue.IsPublished.Should().BeFalse();
        venue.Facilities.Should().Contain("Parking");
    }

    [Fact]
    public void Venue_SetPublished_ShouldThrowIfNotApproved()
    {
        // Arrange
        var venue = Venue.Create(
            Guid.NewGuid(), "Pitch", "pitch", null, Guid.NewGuid(),
            "Addr", "Cairo", "Area", null, null, "+201000000000",
            null, null, DateTime.UtcNow);

        // Act
        var act = () => venue.SetPublished(true, DateTime.UtcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot publish a venue that is not approved*");
    }

    [Fact]
    public void OperatingSchedule_IsOvernight_ShouldBeTrue_WhenCrossingMidnight()
    {
        // Arrange: 10:00 AM to 02:00 AM next day
        var schedule = OperatingSchedule.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DayOfWeek.Friday,
            new TimeOnly(10, 0),
            new TimeOnly(2, 0),
            60,
            DateTime.UtcNow);

        // Assert
        schedule.IsOvernight.Should().BeTrue();
    }

    [Fact]
    public void OperatingSchedule_IsOvernight_ShouldBeFalse_WhenNormalDaytime()
    {
        // Arrange: 10:00 AM to 22:00 PM same day
        var schedule = OperatingSchedule.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DayOfWeek.Saturday,
            new TimeOnly(10, 0),
            new TimeOnly(22, 0),
            60,
            DateTime.UtcNow);

        // Assert
        schedule.IsOvernight.Should().BeFalse();
    }

    [Fact]
    public void BlockedSlot_ShouldThrow_WhenEndBeforeOrEqualStart()
    {
        // Arrange
        var start = DateTime.UtcNow;
        var end = start.AddMinutes(-30);

        // Act
        var act = () => BlockedSlot.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            start,
            end,
            "Maintenance",
            Guid.NewGuid(),
            DateTime.UtcNow);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*End time must be after start time*");
    }

    [Fact]
    public void PriceRule_ShouldCorrectlyHoldMoneyValue()
    {
        // Arrange
        var price = new Money(350.00m, "EGP");
        var rule = PriceRule.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            DayOfWeek.Friday,
            new TimeOnly(18, 0),
            new TimeOnly(23, 59),
            price,
            DateTime.UtcNow);

        // Assert
        rule.Price.Amount.Should().Be(350.00m);
        rule.Price.Currency.Should().Be("EGP");
    }
}
