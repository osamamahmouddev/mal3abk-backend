using FluentAssertions;
using Malabak.Modules.Identity.Application.Profile;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class OnboardingTests
{
    private readonly FixedClock _clock = new();

    [Fact]
    public async Task NewPlayer_FromOtp_HasOnboardingPendingAsync()
    {
        await using var db = NewDb();
        var player = await SeedPlayerAsync(db);

        var status = await new ProfileHandlers(db, _clock).GetOnboardingStatusAsync(player.Id);

        status!.IsComplete.Should().BeFalse();
        status.MissingSteps.Should().Equal("profile");
        status.Language.Should().Be("ar", "Arabic is the default");
    }

    [Fact]
    public async Task CompleteProfile_StoresMinimumData_AndMarksOnboardingDoneAsync()
    {
        await using var db = NewDb();
        var player = await SeedPlayerAsync(db);
        var handlers = new ProfileHandlers(db, _clock);

        var result = await handlers.HandleAsync(new CompleteProfileCommand(
            player.Id, "  Omar   Khaled ", "Cairo", "Nasr City", [PlayerPosition.Midfielder, PlayerPosition.Midfielder], SkillLevel.Intermediate, "en"));

        result.Value.FullName.Should().Be("Omar Khaled");
        result.Value.Positions.Should().Equal("Midfielder");
        result.Value.Language.Should().Be("en");
        result.Value.OnboardingComplete.Should().BeTrue();
        (await handlers.GetOnboardingStatusAsync(player.Id))!.IsComplete.Should().BeTrue();
    }

    [Theory]
    [InlineData("O", "Cairo", "Maadi", null, "Profile.InvalidName")]
    [InlineData("Omar", "", "Maadi", null, "Profile.InvalidPlace")]
    [InlineData("Omar", "Cairo", "Maadi", "fr", "Profile.InvalidLanguage")]
    public async Task CompleteProfile_RejectsInvalidInputAsync(string name, string city, string area, string? language, string expectedCode)
    {
        await using var db = NewDb();
        var player = await SeedPlayerAsync(db);

        var result = await new ProfileHandlers(db, _clock).HandleAsync(new CompleteProfileCommand(player.Id, name, city, area, null, null, language));

        result.Error.Code.Should().Be(expectedCode);
    }

    [Fact]
    public async Task CompleteProfile_TooManyPositions_IsRejectedAsync()
    {
        await using var db = NewDb();
        var player = await SeedPlayerAsync(db);
        PlayerPosition[] positions = [PlayerPosition.Goalkeeper, PlayerPosition.Defender, PlayerPosition.Midfielder, PlayerPosition.Forward, (PlayerPosition)9];

        var result = await new ProfileHandlers(db, _clock).HandleAsync(new CompleteProfileCommand(player.Id, "Omar", "Cairo", "Maadi", positions, null, null));

        result.Error.Should().Be(ProfileErrors.InvalidPositions);
    }

    [Fact]
    public void Clean_StripsBidiOverridesAndControlCharacters()
    {
        ProfileHandlers.Clean("Ali‮gnp.exe\u0007").Should().Be("Aligpn.exe".Replace("gpn", "gnp", StringComparison.Ordinal));
        ProfileHandlers.Clean("  a   b  ").Should().Be("a b");
    }

    [Fact]
    public async Task SetLanguage_ValidatesAndPersistsAsync()
    {
        await using var db = NewDb();
        var player = await SeedPlayerAsync(db);
        var handlers = new ProfileHandlers(db, _clock);

        (await handlers.HandleAsync(new SetLanguageCommand(player.Id, "de"))).Error.Should().Be(ProfileErrors.InvalidLanguage);
        (await handlers.HandleAsync(new SetLanguageCommand(player.Id, "en"))).IsSuccess.Should().BeTrue();
        (await handlers.GetMyProfileAsync(player.Id))!.Language.Should().Be("en");
    }

    [Fact]
    public async Task PatchProfile_ChangesOnlySentFields_AndRequiresCityWithAreaAsync()
    {
        await using var db = NewDb();
        var player = await SeedPlayerAsync(db);
        var handlers = new ProfileHandlers(db, _clock);
        await handlers.HandleAsync(new CompleteProfileCommand(player.Id, "Omar", "Cairo", "Maadi", [PlayerPosition.Defender], SkillLevel.Beginner, null));

        var result = await handlers.HandleAsync(new UpdateMyProfileCommand(player.Id, "Omar Ali", null, null, null, SkillLevel.Advanced, null));

        result.Value.FullName.Should().Be("Omar Ali");
        result.Value.City.Should().Be("cairo");
        result.Value.Positions.Should().Equal("Defender");
        result.Value.SkillLevel.Should().Be("Advanced");
        (await handlers.HandleAsync(new UpdateMyProfileCommand(player.Id, null, "Giza", null, null, null, null))).Error.Should().Be(ProfileErrors.InvalidPlace);
        (await handlers.HandleAsync(new UpdateMyProfileCommand(player.Id, "x", null, null, null, null, null))).Error.Should().Be(ProfileErrors.InvalidName);
    }

    [Fact]
    public async Task CompleteProfile_AcceptsAnAreaMissingFromTheList_AsTypedAsync()
    {
        await using var db = NewDb();
        var player = await SeedPlayerAsync(db);

        var result = await new ProfileHandlers(db, _clock).HandleAsync(new CompleteProfileCommand(player.Id, "Omar", "Cairo", "  التجمع الخامس ", null, null, null));

        result.IsSuccess.Should().BeTrue();
        (result.Value.City, result.Value.Area).Should().Be(("cairo", "التجمع الخامس"));
    }

    [Fact]
    public async Task PatchProfile_BeforeOnboarding_NeedsAPlaceAsync()
    {
        await using var db = NewDb();
        var player = await SeedPlayerAsync(db);

        var result = await new ProfileHandlers(db, _clock).HandleAsync(new UpdateMyProfileCommand(player.Id, "Omar", null, null, null, null, null));

        result.Error.Should().Be(ProfileErrors.InvalidPlace);
    }

    [Fact]
    public async Task PublicProfile_HidesPhoneEmailAndArea_AndRequiresOnboardingAsync()
    {
        await using var db = NewDb();
        var player = await SeedPlayerAsync(db);
        var handlers = new ProfileHandlers(db, _clock);
        var stats = new NoPlayerStatsSummaryProvider();

        (await handlers.GetPublicProfileAsync(player.Id, stats)).Should().BeNull("unfinished profiles aren't discoverable");

        await handlers.HandleAsync(new CompleteProfileCommand(player.Id, "Omar", "Cairo", "Maadi", null, null, null));
        await handlers.HandleAsync(new SetAvatarCommand(player.Id, "/api/v1/media/x/medium"));
        var profile = await handlers.GetPublicProfileAsync(player.Id, stats);

        profile!.City.Should().Be("cairo");
        profile.AvatarUrl.Should().Be("/api/v1/media/x/medium");
        typeof(PublicPlayerProfileDto).GetProperties().Select(p => p.Name).Should().NotContain(["PhoneNumber", "Email", "Area"]);
    }

    [Fact]
    public async Task Avatar_CanBeSetAndRemovedAsync()
    {
        await using var db = NewDb();
        var player = await SeedPlayerAsync(db);
        var handlers = new ProfileHandlers(db, _clock);

        (await handlers.HandleAsync(new SetAvatarCommand(player.Id, "/api/v1/media/a/medium"))).Value.AvatarUrl.Should().NotBeNull();
        (await handlers.HandleAsync(new SetAvatarCommand(player.Id, null))).Value.AvatarUrl.Should().BeNull();
        (await handlers.HandleAsync(new SetAvatarCommand(Guid.NewGuid(), null))).Error.Should().Be(ProfileErrors.NotFound);
    }

    private async Task<User> SeedPlayerAsync(IdentityDbContext db)
    {
        var player = User.CreatePlayer(Guid.NewGuid(), "+201001234567", "Player", _clock.UtcNow);
        db.Users.Add(player);
        await db.SaveChangesAsync();
        return player;
    }

    private static IdentityDbContext NewDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
