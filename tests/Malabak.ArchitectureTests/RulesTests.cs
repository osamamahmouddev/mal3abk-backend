using FluentAssertions;
using Malabak.ArchitectureTests.Rules;
using NetArchTest.Rules;
using Xunit;

namespace Malabak.ArchitectureTests;

public sealed class RulesTests
{
    [Fact]
    public void DoNotUseDateTimeNowOrUtcNowRule_WhenTypeUsesDateTimeUtcNow_FailsNetArchTest()
    {
        // Arrange & Act
        _ = ViolatingClass.GetCurrentTime();

        var result = Types.InAssembly(typeof(RulesTests).Assembly)
            .That()
            .AreClasses()
            .And()
            .HaveName("ViolatingClass")
            .Should()
            .MeetCustomRule(new DoNotUseDateTimeNowOrUtcNowRule())
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeFalse();
    }

    [Fact]
    public void DoNotUseDateTimeNowOrUtcNowRule_WhenTypeDoesNotUseDateTimeUtcNow_PassesNetArchTest()
    {
        // Arrange & Act
        _ = CleanClass.GetGreeting();

        var result = Types.InAssembly(typeof(RulesTests).Assembly)
            .That()
            .AreClasses()
            .And()
            .HaveName("CleanClass")
            .Should()
            .MeetCustomRule(new DoNotUseDateTimeNowOrUtcNowRule())
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue();
    }

    public static class ViolatingClass
    {
        public static DateTime GetCurrentTime() => DateTime.UtcNow;
    }

    public static class CleanClass
    {
        public static string GetGreeting() => "Hello";
    }
}
