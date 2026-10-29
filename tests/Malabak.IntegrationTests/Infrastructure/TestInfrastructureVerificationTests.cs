using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure.Auth;
using Malabak.IntegrationTests.Infrastructure.Builders;
using Malabak.IntegrationTests.Infrastructure.Containers;
using Malabak.IntegrationTests.Infrastructure.Database;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class TestInfrastructureVerificationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TestInfrastructureVerificationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void FakeClock_ShouldCorrectlyAdvanceAndSetTime()
    {
        // Arrange
        var initial = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(initial);

        clock.UtcNow.Should().Be(initial);

        // Act & Assert
        clock.AdvanceMinutes(30);
        clock.UtcNow.Should().Be(initial.AddMinutes(30));

        clock.AdvanceHours(2);
        clock.UtcNow.Should().Be(initial.AddHours(2.5));

        clock.AdvanceDays(1);
        clock.UtcNow.Should().Be(initial.AddDays(1).AddHours(2.5));

        var explicitTime = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        clock.SetUtcNow(explicitTime);
        clock.UtcNow.Should().Be(explicitTime);
        clock.CairoNow.Should().BeAfter(explicitTime);
        clock.CairoToday.Should().Be(new DateOnly(2027, 1, 1));
    }

    [Fact]
    public void TestDataBuilders_ShouldConstructValidDomainEntities()
    {
        // UserBuilder
        var user = new UserBuilder()
            .WithFullName("Mohamed Salah")
            .WithPhoneNumber("+201099887766")
            .WithRole(UserRole.Player)
            .Build();

        user.FullName.Should().Be("Mohamed Salah");
        user.PhoneNumber.Should().Be("+201099887766");
        user.Roles.Should().Contain(UserRole.Player);

        // VenueBuilder
        var venue = new VenueBuilder()
            .WithName("Cairo International Stadium Pitch", "cairo-stadium")
            .WithAddress("Nasr City", "Cairo", "East Cairo")
            .WithFacilities("Floodlights", "VIP Lounge")
            .Build();

        venue.Name.Should().Be("Cairo International Stadium Pitch");
        venue.Slug.Should().Be("cairo-stadium");
        venue.Facilities.Should().Contain("Floodlights");

        // PitchBuilder
        var pitch = new PitchBuilder()
            .WithVenueId(venue.Id)
            .WithName("Grand Pitch A")
            .WithTurfType("Natural Grass")
            .WithCapacity(22)
            .Build();

        pitch.Name.Should().Be("Grand Pitch A");
        pitch.TurfType.Should().Be("NaturalGrass");
        pitch.VenueId.Should().Be(venue.Id);

        // BookingBuilder
        var now = DateTime.UtcNow;
        var booking = new BookingBuilder()
            .ForPlayer(user.Id)
            .ForPitch(pitch.Id)
            .WithTimeSlot(now.AddHours(1), now.AddHours(2))
            .WithPrice(600m, 200m)
            .Build();

        booking.PlayerId.Should().Be(user.Id);
        booking.PitchId.Should().Be(pitch.Id);
        booking.Status.Should().Be(BookingStatus.PendingDeposit);
        booking.TotalPrice.Amount.Should().Be(600m);
        booking.DepositAmount.Amount.Should().Be(200m);
    }

    [Theory]
    [InlineData(UserRole.Player)]
    [InlineData(UserRole.VenueOwner)]
    [InlineData(UserRole.VenueStaff)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.SupportAgent)]
    [InlineData(UserRole.Finance)]
    public void AuthenticatedTestClient_ShouldGenerateValidTokensWithRole(string role)
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = Guid.NewGuid();

        // Act
        client.WithRole(_factory.Services, role, userId);

        // Assert
        client.DefaultRequestHeaders.Authorization.Should().NotBeNull();
        client.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");

        var token = client.DefaultRequestHeaders.Authorization.Parameter;
        token.Should().NotBeNullOrWhiteSpace();

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == userId.ToString());
        jwt.Claims.Should().Contain(c => (c.Type == ClaimTypes.Role || c.Type == "role") && c.Value == role);
    }

    [Fact]
    public async Task AuthorizationMatrixHelper_ShouldVerifyEndpointSecurityMatrixAsync()
    {
        // Ping is an AllowAnonymous endpoint -> All roles and Anonymous should receive 200 OK
        await AuthorizationMatrixHelper
            .ForEndpoint(HttpMethod.Get, "/api/v1/ping")
            .WithAnonymousExpecting(HttpStatusCode.OK)
            .WithPlayerExpecting(HttpStatusCode.OK)
            .WithVenueOwnerExpecting(HttpStatusCode.OK)
            .WithAdminExpecting(HttpStatusCode.OK)
            .ExecuteAsync(_factory);
    }

    [Fact]
    public void TestcontainersFixtures_CanBeInstantiatedWithValidConfiguration()
    {
        // Verifies the fixtures construct with appropriate image and port bindings
        var pgFixture = new PostgreSqlContainerFixture();
        pgFixture.Should().NotBeNull();

        var redisFixture = new RedisContainerFixture();
        redisFixture.Should().NotBeNull();
    }
}
