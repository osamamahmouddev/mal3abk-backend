using System.Reflection;
using FluentAssertions;
using Malabak.ArchitectureTests.Rules;
using NetArchTest.Rules;
using Xunit;

namespace Malabak.ArchitectureTests;

public sealed class ArchitectureTests
{
    private static readonly string[] _infrastructureNamespaces =
    [
        "Malabak.Modules.Booking.Infrastructure",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore"
    ];

    private static readonly string[] _allModuleNamespaces =
    [
        "Malabak.Modules.Audit",
        "Malabak.Modules.Booking",
        "Malabak.Modules.Identity",
        "Malabak.Modules.Matches",
        "Malabak.Modules.Notifications",
        "Malabak.Modules.Payments",
        "Malabak.Modules.Subscriptions",
        "Malabak.Modules.Support",
        "Malabak.Modules.Teams",
        "Malabak.Modules.Venues"
    ];

    private static readonly string[] _locationWords = ["Latitude", "Longitude", "GeoLocation", "Coordinates"];

    private static readonly Assembly[] _moduleAssemblies =
    [
        typeof(Modules.Audit.AuditModule).Assembly,
        typeof(Modules.Booking.Domain.Booking).Assembly,
        typeof(Modules.Identity.IdentityModule).Assembly,
        typeof(Modules.Matches.MatchesModule).Assembly,
        typeof(Modules.Notifications.NotificationsModule).Assembly,
        typeof(Modules.Payments.PaymentsModule).Assembly,
        typeof(Modules.Subscriptions.SubscriptionsModule).Assembly,
        typeof(Modules.Support.SupportModule).Assembly,
        typeof(Modules.Teams.TeamsModule).Assembly,
        typeof(Modules.Venues.VenuesModule).Assembly
    ];

    [Fact]
    public void SharedKernel_ShouldNotDependOn_AnyModuleOrApi()
    {
        // Arrange
        var assembly = typeof(SharedKernel.Result).Assembly;
        var forbiddenDependencies = _allModuleNamespaces.Append("Malabak.Api").ToArray();

        // Act
        var testResult = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenDependencies)
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
    public void DomainLayer_ShouldNotDependOn_InfrastructureOrApi()
    {
        // Arrange
        var bookingAssembly = typeof(Modules.Booking.Domain.Booking).Assembly;

        // Act
        var testResult = Types.InAssembly(bookingAssembly)
            .That()
            .ResideInNamespace("Malabak.Modules.Booking.Domain")
            .ShouldNot()
            .HaveDependencyOnAny(_infrastructureNamespaces)
            .GetResult();

        // Assert
        testResult.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void ApplicationLayer_ShouldNotDependOn_InfrastructureOrApi()
    {
        // Arrange
        var bookingAssembly = typeof(Modules.Booking.Domain.Booking).Assembly;

        // Act
        var testResult = Types.InAssembly(bookingAssembly)
            .That()
            .ResideInNamespace("Malabak.Modules.Booking.Application")
            .ShouldNot()
            .HaveDependencyOnAny("Malabak.Modules.Booking.Infrastructure", "Malabak.Api")
            .GetResult();

        // Assert
        testResult.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Modules_ShouldNotDependOn_OtherModulesDirectly()
    {
        // Assert that each module only depends on SharedKernel and not on other modules
        foreach (var moduleAssembly in _moduleAssemblies)
        {
            var currentModuleName = moduleAssembly.GetName().Name!;
            var otherModules = _allModuleNamespaces
                .Where(ns => !string.Equals(ns, currentModuleName, StringComparison.Ordinal))
                .Append("Malabak.Api")
                .ToArray();

            var testResult = Types.InAssembly(moduleAssembly)
                .ShouldNot()
                .HaveDependencyOnAny(otherModules)
                .GetResult();

            testResult.IsSuccessful.Should().BeTrue(
                $"Module '{currentModuleName}' must not depend directly on other modules or Api.");
        }
    }

    [Fact]
    public void DomainAndApplication_ShouldEnforceIClock_OverDateTimeNowOrUtcNow()
    {
        // Arrange
        var bookingAssembly = typeof(Modules.Booking.Domain.Booking).Assembly;

        // Act
        var testResult = Types.InAssembly(bookingAssembly)
            .That()
            .ResideInNamespace("Malabak.Modules.Booking.Domain")
            .Or()
            .ResideInNamespace("Malabak.Modules.Booking.Application")
            .Should()
            .MeetCustomRule(new DoNotUseDateTimeNowOrUtcNowRule())
            .GetResult();

        // Assert
        testResult.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void AllModules_DomainAndApplication_ShouldEnforceIClock_OverDateTimeNowOrUtcNow()
    {
        foreach (var moduleAssembly in _moduleAssemblies)
        {
            var moduleName = moduleAssembly.GetName().Name!;
            var testResult = Types.InAssembly(moduleAssembly)
                .That()
                .ResideInNamespace($"{moduleName}.Domain")
                .Or()
                .ResideInNamespace($"{moduleName}.Application")
                .Should()
                .MeetCustomRule(new DoNotUseDateTimeNowOrUtcNowRule())
                .GetResult();

            testResult.IsSuccessful.Should().BeTrue(
                $"Domain and Application in module '{moduleName}' must use IClock instead of DateTime.Now, DateTime.UtcNow, or DateTime.Today.");
        }
    }

    [Fact]
    public void AllModules_Domain_ShouldNotDependOn_EntityFrameworkCore()
    {
        foreach (var moduleAssembly in _moduleAssemblies)
        {
            var moduleName = moduleAssembly.GetName().Name!;
            var testResult = Types.InAssembly(moduleAssembly)
                .That()
                .ResideInNamespace($"{moduleName}.Domain")
                .ShouldNot()
                .HaveDependencyOn("Microsoft.EntityFrameworkCore")
                .GetResult();

            testResult.IsSuccessful.Should().BeTrue(
                $"Domain in module '{moduleName}' must be persistence-ignorant and never depend on Microsoft.EntityFrameworkCore.");
        }
    }

    [Fact]
    public void CommandAndQueryHandlers_ShouldHaveHandlerSuffix()
    {
        // Arrange
        var bookingAssembly = typeof(Modules.Booking.Domain.Booking).Assembly;

        // Act
        var testResult = Types.InAssembly(bookingAssembly)
            .That()
            .ImplementInterface(typeof(SharedKernel.ICommandHandler<,>))
            .Or()
            .ImplementInterface(typeof(SharedKernel.IQueryHandler<,>))
            .Should()
            .HaveNameEndingWith("Handler")
            .GetResult();

        // Assert
        testResult.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void OnlyVenues_StoreCoordinates_PlayerLocationIsNeverPersisted()
    {
        // TASK-303: player location is used per request for distance only. Any domain type with a coordinate-like
        // property other than the venue itself would mean someone started storing where players are.
        var offenders = _moduleAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Namespace?.Contains(".Domain", StringComparison.Ordinal) == true && t != typeof(Modules.Venues.Domain.Venue))
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(p => _locationWords.Any(w => p.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();

        offenders.Should().BeEmpty();
    }
}
