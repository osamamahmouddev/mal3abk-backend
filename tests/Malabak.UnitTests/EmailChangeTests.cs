using System.Text.RegularExpressions;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.EmailChange;
using Malabak.Modules.Identity.Application.Profile;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// An owner changing their sign-in email (SECURITY.md §5 rules applied to email) and their name from the venue settings.
/// </summary>
public sealed class EmailChangeTests
{
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc));
    private readonly IOtpSecurityService _otp = new OtpSecurityService(Options.Create(new OtpSecurityOptions { HmacSecret = TestSecrets.OtpHmacSecret }));
    private readonly IStepUpService _stepUp;
    private readonly Mailbox _mail = new();
    private readonly IdentityDbContext _db;
    private readonly User _owner;

    public EmailChangeTests()
    {
        _stepUp = new StepUpService(Options.Create(new StepUpOptions { SecretKey = TestSecrets.StepUpSecretKey }), _clock);
        _db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        _owner = User.CreateVenueOwner(Guid.NewGuid(), "owner@old.test", "+201011112222", "hash", "كابتن سعد", _clock.UtcNow);
        _db.Users.Add(_owner);
        _db.SaveChanges();
    }

    private EmailChangeHandlers Handlers() => new(_db, _stepUp, _otp, _mail, new NoAudit(), _clock, NullLogger<EmailChangeHandlers>.Instance);

    private string StepUp(string scope = StepUpScopes.ChangeEmail) => _stepUp.IssueStepUpToken(_owner.Id, scope);

    [Fact]
    public async Task TheEmailChangesOnlyAfterTheCodeSentToTheNewAddress_AndEverySessionIsSignedOutAsync()
    {
        _db.RefreshTokens.Add(RefreshToken.Create(Guid.NewGuid(), _owner.Id, "live-token", Guid.NewGuid(), _clock.UtcNow.AddDays(7), _clock.UtcNow));
        await _db.SaveChangesAsync();

        (await Handlers().HandleAsync(new StartEmailChangeCommand(_owner.Id, " New@Mail.Test ", StepUp()))).IsSuccess.Should().BeTrue();
        _mail.Sent.Should().ContainSingle(m => m.To == "new@mail.test");
        (await _db.Users.SingleAsync()).Email.Should().Be("owner@old.test", "nothing changes before the code is entered");

        (await Handlers().HandleAsync(new ConfirmEmailChangeCommand(_owner.Id, _mail.LastCode()))).IsSuccess.Should().BeTrue();

        var user = await _db.Users.SingleAsync();
        user.Email.Should().Be("new@mail.test");
        user.IsEmailConfirmed.Should().BeTrue();
        (await _db.RefreshTokens.AllAsync(t => t.IsRevoked)).Should().BeTrue();
        _mail.Sent.Should().Contain(m => m.To == "owner@old.test", "the old address is told");
    }

    [Fact]
    public async Task StartingNeedsAFreshEmailStepUp_NotOneForAnotherScopeAsync()
    {
        (await Handlers().HandleAsync(new StartEmailChangeCommand(_owner.Id, "new@mail.test", StepUp(StepUpScopes.ChangePhone))))
            .Error.Should().Be(EmailChangeErrors.InvalidStepUpToken);
        (await Handlers().HandleAsync(new StartEmailChangeCommand(_owner.Id, "new@mail.test", null))).Error.Should().Be(EmailChangeErrors.InvalidStepUpToken);
        _mail.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAddressAnotherAccountUses_IsRefusedWithoutSayingWhyAsync()
    {
        _db.Users.Add(User.CreateVenueOwner(Guid.NewGuid(), "taken@mail.test", "+201033334444", "hash", "Other", _clock.UtcNow));
        await _db.SaveChangesAsync();

        (await Handlers().HandleAsync(new StartEmailChangeCommand(_owner.Id, "taken@mail.test", StepUp()))).Error.Should().Be(EmailChangeErrors.UnableToUseEmail);
        (await Handlers().HandleAsync(new StartEmailChangeCommand(_owner.Id, "owner@old.test", StepUp()))).Error.Should().Be(EmailChangeErrors.SameEmail);
        (await Handlers().HandleAsync(new StartEmailChangeCommand(_owner.Id, "not-an-email", StepUp()))).Error.Should().Be(EmailChangeErrors.InvalidEmail);
    }

    [Fact]
    public async Task FiveWrongCodesSpendTheRequest_AndAnExpiredOneCannotBeUsedAsync()
    {
        await Handlers().HandleAsync(new StartEmailChangeCommand(_owner.Id, "new@mail.test", StepUp()));
        var right = _mail.LastCode();
        var wrong = right == "000000" ? "111111" : "000000";

        for (var i = 1; i < UserActionToken.MaxEmailChangeAttempts; i++)
        {
            (await Handlers().HandleAsync(new ConfirmEmailChangeCommand(_owner.Id, wrong))).Error.Should().Be(EmailChangeErrors.InvalidCode);
        }

        (await Handlers().HandleAsync(new ConfirmEmailChangeCommand(_owner.Id, wrong))).Error.Should().Be(EmailChangeErrors.TooManyAttempts);
        (await Handlers().HandleAsync(new ConfirmEmailChangeCommand(_owner.Id, right))).Error.Should().Be(EmailChangeErrors.NoPendingChange);

        await Handlers().HandleAsync(new StartEmailChangeCommand(_owner.Id, "new@mail.test", StepUp()));
        _clock.Advance(UserActionToken.EmailChangeLifetime + TimeSpan.FromSeconds(1));
        (await Handlers().HandleAsync(new ConfirmEmailChangeCommand(_owner.Id, _mail.LastCode()))).Error.Should().Be(EmailChangeErrors.NoPendingChange);
        (await _db.Users.SingleAsync()).Email.Should().Be("owner@old.test");
    }

    [Fact]
    public async Task ANewRequestReplacesThePreviousOneAsync()
    {
        await Handlers().HandleAsync(new StartEmailChangeCommand(_owner.Id, "first@mail.test", StepUp()));
        var firstCode = _mail.LastCode();
        await Handlers().HandleAsync(new StartEmailChangeCommand(_owner.Id, "second@mail.test", StepUp()));

        (await Handlers().HandleAsync(new ConfirmEmailChangeCommand(_owner.Id, _mail.LastCode()))).IsSuccess.Should().BeTrue();
        (await _db.Users.SingleAsync()).Email.Should().Be("second@mail.test");
        firstCode.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AnOwnerCanRenameThemselves_WithoutAPlayerProfileAsync()
    {
        var profiles = new ProfileHandlers(_db, _clock);

        var renamed = await profiles.HandleAsync(new UpdateMyProfileCommand(_owner.Id, "  كابتن سعد صلاح ", null, null, null, null, null));

        renamed.IsSuccess.Should().BeTrue();
        (await _db.Users.SingleAsync()).FullName.Should().Be("كابتن سعد صلاح");
        (await profiles.HandleAsync(new UpdateMyProfileCommand(_owner.Id, "x", null, null, null, null, null))).Error.Should().Be(ProfileErrors.InvalidName);
    }

    [Fact]
    public async Task APlayerWithoutACity_StillCannotSkipTheirPlaceAsync()
    {
        var player = User.CreatePlayer(Guid.NewGuid(), "+201055556666", "Player", _clock.UtcNow);
        _db.Users.Add(player);
        await _db.SaveChangesAsync();

        (await new ProfileHandlers(_db, _clock).HandleAsync(new UpdateMyProfileCommand(player.Id, "New Name", null, null, null, null, null)))
            .Error.Should().Be(ProfileErrors.InvalidPlace);
    }

    private sealed class Mailbox : IEmailSender
    {
        public List<(string To, string Text)> Sent { get; } = [];

        public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default)
        {
            Sent.Add((toAddress, body));
            return Task.CompletedTask;
        }

        public Task SendAsync(string toAddress, RenderedEmail email, CancellationToken cancellationToken = default)
        {
            Sent.Add((toAddress, email.Text));
            return Task.CompletedTask;
        }

        public string LastCode() => Regex.Match(Sent.Last(m => m.Text.Contains("15")).Text, @"\b\d{6}\b").Value;
    }

    private sealed class NoAudit : IAuditLog
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
