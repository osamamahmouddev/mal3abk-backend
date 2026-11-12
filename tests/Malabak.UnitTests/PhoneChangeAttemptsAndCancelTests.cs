using FluentAssertions;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.PhoneChange.CancelPhoneChange;
using Malabak.Modules.Identity.Application.PhoneChange.VerifyNewPhone;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>SECURITY.md §5.3–5.4: per-code attempt counters, the Failed/Cancelled states and the cancel endpoint's handler.</summary>
public sealed class PhoneChangeAttemptsAndCancelTests
{
    private const string _oldPhone = "+201011112222";
    private const string _newPhone = "+201099998888";

    private readonly FakeClock _clock = new(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc));
    private readonly IOtpSecurityService _otp = new OtpSecurityService(Options.Create(new OtpSecurityOptions { HmacSecret = TestSecrets.OtpHmacSecret }));

    private static IdentityDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private VerifyNewPhoneHandler CreateVerifyNew(IdentityDbContext db)
    {
        var jwt = Options.Create(new JwtOptions { CurrentKeyId = "test-key-v1" });
        return new VerifyNewPhoneHandler(
            db, _otp, new TokenService(new JwtKeyProvider(jwt), _clock, jwt), new NoopAlerts(), _clock, NullLogger<VerifyNewPhoneHandler>.Instance);
    }

    private async Task<(User User, PhoneChangeRequest Request)> SeedOldVerifiedAsync(IdentityDbContext db, string code)
    {
        var user = User.CreatePlayer(Guid.NewGuid(), _oldPhone, "Player", _clock.UtcNow);
        var request = PhoneChangeRequest.Start(Guid.NewGuid(), user.Id, _oldPhone, _newPhone, "old", _clock.UtcNow.AddMinutes(15), _clock.UtcNow);
        request.MarkOldPhoneVerified(_otp.ComputeHmacHash(code), _clock.UtcNow);
        db.Users.Add(user);
        db.PhoneChangeRequests.Add(request);
        await db.SaveChangesAsync();
        return (user, request);
    }

    [Fact]
    public async Task VerifyNewPhone_After5WrongCodes_FailsRequestEvenForTheCorrectCodeAsync()
    {
        await using var db = CreateDbContext();
        var (user, request) = await SeedOldVerifiedAsync(db, "123456");
        var handler = CreateVerifyNew(db);

        for (var i = 0; i < 5; i++)
        {
            (await handler.HandleAsync(new VerifyNewPhoneCommand(user.Id, request.Id, "000000"))).IsFailure.Should().BeTrue();
        }

        var result = await handler.HandleAsync(new VerifyNewPhoneCommand(user.Id, request.Id, "123456"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.PhoneChangeMaxAttemptsExceeded");
        var inDb = await db.PhoneChangeRequests.SingleAsync(r => r.Id == request.Id);
        inDb.Status.Should().Be(PhoneChangeStatus.Failed);
        inDb.AttemptsNew.Should().Be(5);
        (await db.Users.SingleAsync(u => u.Id == user.Id)).PhoneNumber.Should().Be(_oldPhone);
    }

    [Fact]
    public async Task VerifyNewPhone_FewerThan5WrongCodes_StillAllowsTheCorrectCodeAsync()
    {
        await using var db = CreateDbContext();
        var (user, request) = await SeedOldVerifiedAsync(db, "123456");
        var handler = CreateVerifyNew(db);

        for (var i = 0; i < 4; i++)
        {
            await handler.HandleAsync(new VerifyNewPhoneCommand(user.Id, request.Id, "000000"));
        }

        (await handler.HandleAsync(new VerifyNewPhoneCommand(user.Id, request.Id, "123456"))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Cancel_OpenRequest_MarksItCancelledAndItCanNoLongerBeVerifiedAsync()
    {
        await using var db = CreateDbContext();
        var (user, request) = await SeedOldVerifiedAsync(db, "123456");
        var cancel = new CancelPhoneChangeHandler(db, _clock, NullLogger<CancelPhoneChangeHandler>.Instance);

        var result = await cancel.HandleAsync(new CancelPhoneChangeCommand(user.Id, request.Id));

        result.IsSuccess.Should().BeTrue();
        (await db.PhoneChangeRequests.SingleAsync(r => r.Id == request.Id)).Status.Should().Be(PhoneChangeStatus.Cancelled);
        (await CreateVerifyNew(db).HandleAsync(new VerifyNewPhoneCommand(user.Id, request.Id, "123456"))).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Cancel_AnotherUsersRequest_IsNotFoundAsync()
    {
        await using var db = CreateDbContext();
        var (_, request) = await SeedOldVerifiedAsync(db, "123456");
        var cancel = new CancelPhoneChangeHandler(db, _clock, NullLogger<CancelPhoneChangeHandler>.Instance);

        var result = await cancel.HandleAsync(new CancelPhoneChangeCommand(Guid.NewGuid(), request.Id));

        result.Error.Code.Should().Be("Identity.PhoneChangeRequestNotFound");
        (await db.PhoneChangeRequests.SingleAsync(r => r.Id == request.Id)).Status.Should().Be(PhoneChangeStatus.OldVerified);
    }

    [Fact]
    public async Task Cancel_CompletedRequest_IsRejectedAsync()
    {
        await using var db = CreateDbContext();
        var (user, request) = await SeedOldVerifiedAsync(db, "123456");
        request.Complete("recovery", _clock.UtcNow);
        await db.SaveChangesAsync();
        var cancel = new CancelPhoneChangeHandler(db, _clock, NullLogger<CancelPhoneChangeHandler>.Instance);

        var result = await cancel.HandleAsync(new CancelPhoneChangeCommand(user.Id, request.Id));

        result.Error.Code.Should().Be("Identity.InvalidPhoneChangeState");
    }

    private sealed class NoopAlerts : ISmsAlertService
    {
        public Task SendSecurityAlertAsync(string phoneNumber, SecurityAlert alert, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
