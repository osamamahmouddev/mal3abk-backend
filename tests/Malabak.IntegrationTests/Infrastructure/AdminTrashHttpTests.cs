using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Venues.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// The admin trash through the real API, PostgreSQL and every module: deleting signs a player out at once, restoring brings
/// them back, purging erases their personal data; a venue needs its name typed and goes off the app; support cannot touch it.
/// </summary>
public sealed class AdminTrashHttpTests : IClassFixture<BookingApiFactory>
{
    private readonly BookingApiFactory _factory;

    public AdminTrashHttpTests(BookingApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task APlayer_IsDeletedRestoredAndPurged_WithStepUpAndTheNameTypedAsync()
    {
        var (adminId, admin) = await _factory.CreateAdminAsync();
        var (playerId, player) = await _factory.CreatePlayerAsync();
        var name = await PlayerNameAsync(playerId);

        var withoutStepUp = await admin.PostAsJsonAsync($"/api/v1/admin/trash/items/user/{playerId}", new { reason = "حساب وهمي" });
        withoutStepUp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var deleted = await SendAsync(admin, adminId, $"/api/v1/admin/trash/items/user/{playerId}", new { reason = "حساب وهمي بيحجز ومبيجيش" });
        deleted.StatusCode.Should().Be(HttpStatusCode.OK, await deleted.Content.ReadAsStringAsync());
        (await player.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a deleted account is signed out everywhere");

        var entryId = await EntryIdAsync(admin, playerId);
        (await admin.PostAsync($"/api/v1/admin/trash/{entryId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await UserAsync(playerId)).IsDeleted.Should().BeFalse();

        (await SendAsync(admin, adminId, $"/api/v1/admin/trash/items/user/{playerId}", new { reason = "طلب حذف بياناته" })).EnsureSuccessStatusCode();
        entryId = await EntryIdAsync(admin, playerId);
        (await SendAsync(admin, adminId, $"/api/v1/admin/trash/{entryId}/purge", new { confirmName = "اسم غلط" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(admin, adminId, $"/api/v1/admin/trash/{entryId}/purge", new { confirmName = name })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var purged = await UserAsync(playerId);
        purged.FullName.Should().Be(User.AnonymizedName);
        purged.AnonymizedAtUtc.Should().NotBeNull();
        (await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/trash")).EnumerateArray()
            .Should().NotContain(e => e.GetProperty("entityId").GetGuid() == playerId);
    }

    [Fact]
    public async Task AVenue_NeedsItsNameTyped_AndItsOwnerCannotBeDeletedWhileItExistsAsync()
    {
        var (adminId, admin) = await _factory.CreateAdminAsync();
        var (ownerId, venueId, _, _) = await _factory.CreateVenueOwnerAsync();

        (await SendAsync(admin, adminId, $"/api/v1/admin/trash/items/user/{ownerId}", new { reason = "صاحب ملعب مكرر" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "an owner's venues go first");
        (await SendAsync(admin, adminId, $"/api/v1/admin/trash/items/venue/{venueId}", new { reason = "ملعب مقفول", confirmName = "غلط" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(admin, adminId, $"/api/v1/admin/trash/items/venue/{venueId}", new { reason = "ملعب مقفول", confirmName = "Test Club" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var venue = await scope.ServiceProvider.GetRequiredService<VenuesDbContext>().Venues.IgnoreQueryFilters().SingleAsync(v => v.Id == venueId);
            venue.IsDeleted.Should().BeTrue();
            venue.IsPublished.Should().BeFalse("it is off the app at once");
        }

        var entryId = await EntryIdAsync(admin, venueId);
        (await admin.PostAsync($"/api/v1/admin/trash/{entryId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SupportAgents_CannotSeeOrUseTheTrashAsync()
    {
        var (_, support) = await _factory.CreateSupportAgentAsync();

        (await support.GetAsync("/api/v1/admin/trash")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpClient client, Guid adminId, string url, object body)
    {
        string stamp;
        using (var scope = _factory.Services.CreateScope())
        {
            stamp = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.Where(u => u.Id == adminId).Select(u => u.SecurityStamp).SingleAsync();
        }

        var token = _factory.Services.GetRequiredService<IStepUpService>().IssueStepUpToken(adminId, "admin_destructive", TimeSpan.FromMinutes(5), stamp);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Step-Up-Token", token);
        return await client.SendAsync(request);
    }

    private static async Task<Guid> EntryIdAsync(HttpClient admin, Guid entityId) =>
        (await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/trash")).EnumerateArray()
            .Single(e => e.GetProperty("entityId").GetGuid() == entityId).GetProperty("id").GetGuid();

    private async Task<User> UserAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == id);
    }

    private async Task<string> PlayerNameAsync(Guid id) => (await UserAsync(id)).FullName;
}
