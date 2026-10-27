using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Malabak.ArchitectureTests;

public sealed class ArchitectureTests
{
    [Fact]
    public void SharedKernel_ShouldNotDependOn_AnyModule()
    {
        // Arrange
        var assembly = typeof(SharedKernel.Result).Assembly;

        // Act
        var testResult = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Malabak.Modules.Booking",
                "Malabak.Modules.Identity",
                "Malabak.Modules.Venues",
                "Malabak.Api")
            .GetResult();

        // Assert
        testResult.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void IdentityDomain_ShouldNotDependOn_InfrastructureOrApi()
    {
        // Arrange
        var assembly = typeof(Modules.Identity.Domain.User).Assembly;

        // Act
        var testResult = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace("Malabak.Modules.Identity.Domain")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Malabak.Modules.Identity.Infrastructure",
                "Malabak.Api",
                "Microsoft.EntityFrameworkCore")
            .GetResult();

        // Assert
        testResult.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void VenuesDomain_ShouldNotDependOn_InfrastructureOrApi()
    {
        // Arrange
        var assembly = typeof(Modules.Venues.Domain.Venue).Assembly;

        // Act
        var testResult = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace("Malabak.Modules.Venues.Domain")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Malabak.Modules.Venues.Infrastructure",
                "Malabak.Api",
                "Microsoft.EntityFrameworkCore")
            .GetResult();

        // Assert
        testResult.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void BookingDomain_ShouldNotDependOn_InfrastructureOrApi()
    {
        // Arrange
        var assembly = typeof(Modules.Booking.Domain.Booking).Assembly;

        // Act
        var testResult = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace("Malabak.Modules.Booking.Domain")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Malabak.Modules.Booking.Infrastructure",
                "Malabak.Api",
                "Microsoft.EntityFrameworkCore")
            .GetResult();

        // Assert
        testResult.IsSuccessful.Should().BeTrue();
    }
}
