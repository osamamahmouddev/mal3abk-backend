using FluentAssertions;
using Malabak.Modules.Identity.Application.Authentication.VerifyOtp;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>ADR-0007: signing in with the number of an account pending deletion never re-registers it.</summary>
public sealed class VerifyOtpDeletedAccountTests
{
    private const string _rawPhone = "01012345678";
    private const string _normalizedPhone = "+201012345678";
    private const string _code = "123456";

    private readonly IOtpSecurityService _securityService =
        new OtpSecurityService(Options.Create(new OtpSecurityOptions { HmacSecret = TestSecrets.OtpHmacSecret }));
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 6, 20, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task VerifyOtp_ForAccountPendingDeletion_AsksToRestoreInsteadOfRegisteringAsync()
    {
        await using var db = CreateDbContext();
        var userId = await SeedDeletedPlayerAsync(db);
        var handler = await CreateHandlerAsync(db);

        var result = await handler.HandleAsync(new VerifyOtpCommand(_rawPhone, _code));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.AccountPendingDeletion");
        (await db.Users.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.Users.IgnoreQueryFilters().SingleAsync()).Id.Should().Be(userId);
    }

    [Fact]
    public async Task VerifyOtp_WithRestoreConfirmed_RestoresTheSameAccountAsync()
    {
        await using var db = CreateDbContext();
        var userId = await SeedDeletedPlayerAsync(db);
        var handler = await CreateHandlerAsync(db);

        var result = await handler.HandleAsync(new VerifyOtpCommand(_rawPhone, _code, RestoreAccount: true));

        result.IsSuccess.Should().BeTrue();
        result.Value.IsNewUser.Should().BeFalse();
        result.Value.UserId.Should().Be(userId.ToString());
        (await db.Users.SingleAsync()).IsDeleted.Should().BeFalse();
    }

    private async Task<Guid> SeedDeletedPlayerAsync(IdentityDbContext db)
    {
        var user = User.CreatePlayer(Guid.NewGuid(), _normalizedPhone, "Player", _clock.UtcNow.AddDays(-10));
        user.RequestDeletion(_clock.UtcNow.AddDays(-1));
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<VerifyOtpHandler> CreateHandlerAsync(IdentityDbContext db)
    {
        var store = new InMemoryOtpStore();
        await store.SaveOtpAsync(_normalizedPhone, _securityService.ComputeHmacHash(_code), TimeSpan.FromMinutes(5));
        return new VerifyOtpHandler(new PhoneNumberNormalizer(), _securityService, store, db, _clock, NullLogger<VerifyOtpHandler>.Instance);
    }

    private static IdentityDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
