using FluentAssertions;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>The local Firebase key path is written once with "~" and must resolve on every developer's machine.</summary>
public sealed class FirebaseKeyPathTests
{
    private static readonly string _home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void ExpandHomeDirectory_TildePath_ResolvesUnderTheUserProfile()
    {
        var path = Program.ExpandHomeDirectory("~/.config/mal3abk/firebase-service-account.json");

        path.Should().Be(Path.Join(_home, ".config/mal3abk/firebase-service-account.json"));
    }

    [Theory]
    [InlineData("/etc/secrets/firebase.json")]
    [InlineData(@"C:\keys\firebase.json")]
    public void ExpandHomeDirectory_AbsolutePath_IsUnchanged(string path)
    {
        Program.ExpandHomeDirectory(path).Should().Be(path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ExpandHomeDirectory_Empty_StaysEmpty(string? path)
    {
        Program.ExpandHomeDirectory(path).Should().Be(path);
    }
}
