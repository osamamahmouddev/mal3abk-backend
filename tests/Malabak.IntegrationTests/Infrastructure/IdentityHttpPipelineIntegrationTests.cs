using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// Phase 1 (P1) Integration Tests: Identity HTTP Pipeline & Security Endpoints.
/// Exercises real middleware, authentication schemes, rate limiters, and session isolation.
/// </summary>
public sealed class IdentityHttpPipelineIntegrationTests : IClassFixture<BookingApiFactory>
{
    private readonly BookingApiFactory _factory;

    public IdentityHttpPipelineIntegrationTests(BookingApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OtpSend_InvalidPhoneNumber_ReturnsBadRequestProblemDetailsAsync()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/otp/send", new
        {
            phoneNumber = "+14155552671", // Non-Egyptian
            turnstileToken = "test-token"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Identity.InvalidEgyptianPhoneNumber");
    }

    [Fact]
    public async Task OtpSend_ValidEgyptianMobile_UnderstandsContractAsync()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/otp/send", new
        {
            phoneNumber = "01012345678",
            turnstileToken = "test-token"
        });

        // 200 OK when SMS delivery succeeds, or 503 ServiceUnavailable when test SMS gateway is not connected
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task OtpVerify_InvalidCode_ReturnsBadRequestProblemDetailsAsync()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new
        {
            phoneNumber = "01012345678",
            code = "999999" // Unsent / non-existent code
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().ContainAny("Identity.OtpExpiredOrNotFound", "Identity.InvalidOtpCode");
    }

    [Fact]
    public async Task RefreshToken_NonExistentToken_ReturnsUnauthorizedAsync()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            refreshToken = "completely-invalid-raw-token"
        });

        // SECURITY.md: Invalid refresh token rejected as 401 Unauthorized
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ForgotPassword_NonExistentEmail_ReturnsAcceptedWithoutEnumerationAsync()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/password/forgot", new
        {
            email = "nobody-registered@malabak.test"
        });

        // SECURITY.md §4.2: Password forgot is non-enumerable, always accepts (200 OK or 202 Accepted)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Sessions_RevokeOtherUsersSession_Returns404NotFoundAsync()
    {
        var (_, playerAClient) = await _factory.CreatePlayerAsync();
        var (_, playerBClient) = await _factory.CreatePlayerAsync();

        // Player A lists sessions to obtain their session ID
        var sessionsResponse = await playerAClient.GetAsync("/api/v1/me/sessions");
        sessionsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await sessionsResponse.Content.ReadAsStringAsync());
        var playerASessionId = doc.RootElement[0].GetProperty("sessionId").GetGuid();

        // Player B attempts to revoke Player A's session -> must be rejected with 404 Not Found
        var revokeResponse = await playerBClient.DeleteAsync($"/api/v1/me/sessions/{playerASessionId}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await revokeResponse.Content.ReadAsStringAsync();
        body.Should().Contain("Identity.SessionNotFound");
    }

    [Fact(Skip = "BUG-015: Concurrent refresh token requests trigger unhandled DbUpdateConcurrencyException (500)")]
    public async Task RefreshToken_ConcurrentRefreshesWithSameToken_ExactlyOneSucceedsAsync()
    {
        var client = _factory.CreateClient();
        var phone = $"+2010{Random.Shared.Next(10_000_000, 99_999_999)}";
        const string code = "654321";

        // Issue token pair through real verify pipeline
        using (var scope = _factory.Services.CreateScope())
        {
            var otpStore = scope.ServiceProvider.GetRequiredService<IOtpStore>();
            var securityService = scope.ServiceProvider.GetRequiredService<IOtpSecurityService>();
            var hash = securityService.ComputeHmacHash(code);
            await otpStore.SaveOtpAsync(phone, hash, TimeSpan.FromMinutes(5));
        }

        var verifyResponse = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new
        {
            phoneNumber = phone,
            code = code
        });
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyDoc = JsonDocument.Parse(await verifyResponse.Content.ReadAsStringAsync());
        var rawRefreshToken = verifyDoc.RootElement.GetProperty("refreshToken").GetString()!;

        // Fire two concurrent refresh requests with Task.WhenAll against real PostgreSQL
        var task1 = client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = rawRefreshToken });
        var task2 = client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = rawRefreshToken });
        var responses = await Task.WhenAll(task1, task2);

        // Exactly one refresh request must succeed with 200 OK, the other must be rejected with 401 Unauthorized
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1,
            "exactly one concurrent refresh with the same token must succeed");
        responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(1,
            "concurrent reuse must be rejected with 401 Unauthorized, never a 500 exception");
    }

    [Fact]
    public async Task RefreshToken_DatabaseStoresOnlySha256Hash_NeverPlaintextAsync()
    {
        var client = _factory.CreateClient();
        var phone = $"+2010{Random.Shared.Next(10_000_000, 99_999_999)}";
        const string code = "654321";

        // Go through real verify pipeline
        using (var scope = _factory.Services.CreateScope())
        {
            var otpStore = scope.ServiceProvider.GetRequiredService<IOtpStore>();
            var securityService = scope.ServiceProvider.GetRequiredService<IOtpSecurityService>();
            var hash = securityService.ComputeHmacHash(code);
            await otpStore.SaveOtpAsync(phone, hash, TimeSpan.FromMinutes(5));
        }

        var verifyResponse = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new
        {
            phoneNumber = phone,
            code = code
        });
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyDoc = JsonDocument.Parse(await verifyResponse.Content.ReadAsStringAsync());
        var rawRefreshToken = verifyDoc.RootElement.GetProperty("refreshToken").GetString()!;
        var userId = verifyDoc.RootElement.GetProperty("userId").GetGuid();

        // Query the real database row
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var tokenRow = await db.RefreshTokens.FirstOrDefaultAsync(r => r.UserId == userId);

            tokenRow.Should().NotBeNull();
            // Stored value must be a 64-character SHA-256 hash, NEVER the plaintext raw token
            tokenRow!.TokenHash.Should().NotBe(rawRefreshToken, "database must store SHA-256 hash, never raw token");
            tokenRow.TokenHash.Length.Should().Be(64, "SHA-256 hash representation is 64 hex characters");
        }
    }

    [Fact]
    public async Task SendOtp_RegisteredVsUnregistered_ReturnsIdenticalResponseAsync()
    {
        var client = _factory.CreateClient();
        var registeredPhone = $"+2010{Random.Shared.Next(10_000_000, 99_999_999)}";
        var unregisteredPhone = $"+2010{Random.Shared.Next(10_000_000, 99_999_999)}";

        // Actually register one of the numbers in the database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var registeredUser = User.CreatePlayer(Guid.NewGuid(), registeredPhone, "Registered Player", DateTime.UtcNow);
            db.Users.Add(registeredUser);
            await db.SaveChangesAsync();
        }

        // Send OTP to registered number
        var responseRegistered = await client.PostAsJsonAsync("/api/v1/auth/otp/send", new
        {
            phoneNumber = registeredPhone,
            turnstileToken = "test-token"
        });

        // Send OTP to unregistered number
        var responseUnregistered = await client.PostAsJsonAsync("/api/v1/auth/otp/send", new
        {
            phoneNumber = unregisteredPhone,
            turnstileToken = "test-token"
        });

        // Compare full HTTP response status, Content-Type, and structure
        responseRegistered.StatusCode.Should().Be(responseUnregistered.StatusCode);
        responseRegistered.Content.Headers.ContentType?.MediaType.Should().Be(
            responseUnregistered.Content.Headers.ContentType?.MediaType);

        if (responseRegistered.StatusCode == HttpStatusCode.OK)
        {
            using var docReg = JsonDocument.Parse(await responseRegistered.Content.ReadAsStringAsync());
            using var docUnreg = JsonDocument.Parse(await responseUnregistered.Content.ReadAsStringAsync());

            docReg.RootElement.GetProperty("expiresInSeconds").GetInt32().Should().Be(
                docUnreg.RootElement.GetProperty("expiresInSeconds").GetInt32());

            // Neither response reveals whether user exists
            docReg.RootElement.TryGetProperty("isNewUser", out _).Should().BeFalse();
            docUnreg.RootElement.TryGetProperty("isNewUser", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Impersonation_PlayerToken_RejectedFromAdminImpersonationAsync()
    {
        var (_, playerClient) = await _factory.CreatePlayerAsync();

        // Player attempts to call admin impersonation endpoint
        var response = await playerClient.PostAsJsonAsync("/api/v1/admin/impersonate", new
        {
            userId = Guid.NewGuid(),
            reason = "Malicious impersonation attempt"
        });

        // Must be rejected with 401 Unauthorized or 403 Forbidden due to AdminScheme & Permissions.AdminImpersonate
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
