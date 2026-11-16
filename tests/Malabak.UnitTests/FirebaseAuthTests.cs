using FluentAssertions;
using Malabak.Modules.Identity.Application.Authentication.FirebaseAuth;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class FirebaseAuthTests
{
    private readonly IPhoneNumberNormalizer _normalizer = new PhoneNumberNormalizer();
    private readonly FakeClock _clock = new();

    private sealed class FakeClock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        public DateTime UtcNow { get; set; } = DateTime.UtcNow;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }

    private static IdentityDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(databaseName: $"malabak_firebase_test_{Guid.NewGuid():N}")
            .Options;
        return new IdentityDbContext(options);
    }

    [Fact]
    public async Task HandleAsync_WithValidFirebaseToken_CreatesNewPlayerUserAsync()
    {
        await using var db = CreateDb();
        var fakeAuth = new FakeFirebaseAuthService(new FirebaseTokenClaims("firebase-uid-1", "+201012345678", null, "Ahmed Ali"));
        var handler = new FirebaseAuthHandler(
            fakeAuth,
            _normalizer,
            db,
            _clock,
            NullLogger<FirebaseAuthHandler>.Instance);

        var result = await handler.HandleAsync(new FirebaseAuthCommand("valid-token", "Ahmed Ali"));

        result.IsSuccess.Should().BeTrue();
        result.Value.PhoneNumber.Should().Be("+201012345678");
        result.Value.FullName.Should().Be("Ahmed Ali");
        result.Value.IsNewUser.Should().BeTrue();

        var savedUser = await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == "+201012345678");
        savedUser.Should().NotBeNull();
        savedUser!.FullName.Should().Be("Ahmed Ali");
    }

    [Fact]
    public async Task HandleAsync_WithExistingUser_AuthenticatesWithoutCreatingDuplicateAsync()
    {
        await using var db = CreateDb();
        var existingUser = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Existing Player", _clock.UtcNow);
        db.Users.Add(existingUser);
        await db.SaveChangesAsync();

        var fakeAuth = new FakeFirebaseAuthService(new FirebaseTokenClaims("firebase-uid-2", "+201012345678", null, null));
        var handler = new FirebaseAuthHandler(
            fakeAuth,
            _normalizer,
            db,
            _clock,
            NullLogger<FirebaseAuthHandler>.Instance);

        var result = await handler.HandleAsync(new FirebaseAuthCommand("valid-token"));

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(existingUser.Id);
        result.Value.IsNewUser.Should().BeFalse();
        (await db.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_WhenFirebaseReturnsInvalidToken_FailsWithExpectedErrorAsync()
    {
        await using var db = CreateDb();
        var fakeAuth = new FakeFirebaseAuthService(error: new Error("Identity.InvalidFirebaseToken", "Invalid or expired Firebase ID token."));
        var handler = new FirebaseAuthHandler(
            fakeAuth,
            _normalizer,
            db,
            _clock,
            NullLogger<FirebaseAuthHandler>.Instance);

        var result = await handler.HandleAsync(new FirebaseAuthCommand("invalid-token"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.InvalidFirebaseToken");
    }

    [Fact]
    public async Task HandleAsync_WhenUserIsSuspended_RefusesSignInAsync()
    {
        await using var db = CreateDb();
        var user = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Suspended User", _clock.UtcNow);
        user.Suspend("TOS violation", _clock.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var fakeAuth = new FakeFirebaseAuthService(new FirebaseTokenClaims("firebase-uid-3", "+201012345678", null, null));
        var handler = new FirebaseAuthHandler(
            fakeAuth,
            _normalizer,
            db,
            _clock,
            NullLogger<FirebaseAuthHandler>.Instance);

        var result = await handler.HandleAsync(new FirebaseAuthCommand("valid-token"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.AccountSuspended");
    }

    private sealed class FakeFirebaseAuthService : IFirebaseAuthService
    {
        private readonly FirebaseTokenClaims? _claims;
        private readonly Error? _error;

        public FakeFirebaseAuthService(FirebaseTokenClaims? claims = null, Error? error = null)
        {
            _claims = claims;
            _error = error;
        }

        public Task<Result<FirebaseTokenClaims>> VerifyIdTokenAsync(string idToken, CancellationToken cancellationToken = default)
        {
            if (_error != null)
            {
                return Task.FromResult(Result.Failure<FirebaseTokenClaims>(_error));
            }

            return Task.FromResult(Result.Success(_claims!));
        }
    }
}
