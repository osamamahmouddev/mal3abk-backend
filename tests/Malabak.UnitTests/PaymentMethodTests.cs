using System.Text;
using FluentAssertions;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.PaymentMethods;
using Malabak.Modules.Venues.Application.PaymentMethods.CreatePaymentMethod;
using Malabak.Modules.Venues.Application.PaymentMethods.DeletePaymentMethod;
using Malabak.Modules.Venues.Application.PaymentMethods.GetPaymentMethods;
using Malabak.Modules.Venues.Application.PaymentMethods.RevealPaymentMethodDetails;
using Malabak.Modules.Venues.Application.PaymentMethods.UpdatePaymentMethod;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.Modules.Venues.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class PaymentMethodTests
{
    private const string _iban = "EG380019000500000000263180002";

    private readonly TestClock _clock = new();
    private readonly FakeCipher _cipher = new();
    private readonly RecordingAuditLogger _audit = new();
    private readonly Guid _ownerId = Guid.NewGuid();

    [Theory]
    [InlineData(PaymentMethodType.BankTransfer, "EG38 0019 0005 0000 0000 2631 80002", true)]
    [InlineData(PaymentMethodType.BankTransfer, "1234567890", true)]
    [InlineData(PaymentMethodType.BankTransfer, "abc", false)]
    [InlineData(PaymentMethodType.MobileWallet, "01012345678", true)]
    [InlineData(PaymentMethodType.MobileWallet, "+201112345678", true)]
    [InlineData(PaymentMethodType.MobileWallet, "01312345678", false)]
    [InlineData(PaymentMethodType.InstaPay, "malabak.club@instapay", true)]
    [InlineData(PaymentMethodType.InstaPay, "not-an-address", false)]
    public void AccountDetails_FormatValidation(string type, string details, bool expected)
    {
        PaymentMethodType.IsValidAccountDetails(type, PaymentMethodType.Normalize(details)).Should().Be(expected);
    }

    [Theory]
    [InlineData(DepositType.Percentage, 30, 500, 150)]
    [InlineData(DepositType.Percentage, 100, 500, 500)]
    [InlineData(DepositType.FixedAmount, 200, 500, 200)]
    [InlineData(DepositType.FixedAmount, 800, 500, 500)]
    [InlineData(DepositType.Full, 0, 500, 500)]
    public void DepositRule_CalculatesDeposit_NeverAbovePrice(DepositType type, decimal value, decimal price, decimal expected)
    {
        DepositRule.Create(type, value).CalculateDeposit(price).Should().Be(expected);
    }

    [Theory]
    [InlineData(DepositType.Percentage, 0)]
    [InlineData(DepositType.Percentage, 101)]
    [InlineData(DepositType.FixedAmount, -5)]
    [InlineData(DepositType.FixedAmount, 10.555)]
    public void DepositRule_InvalidValues_Throw(DepositType type, decimal value)
    {
        var act = () => DepositRule.Create(type, value);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Mask_ShowsOnlyLastFourCharacters()
    {
        VenuePaymentMethod.Mask(_iban).Should().Be("•••• 0002");
        VenuePaymentMethod.Mask("123").Should().Be("••••");
    }

    [Fact]
    public async Task Create_EncryptsDetails_StoresOnlyCiphertextAndMask_AndAuditsAsync()
    {
        await using var db = await SeedAsync();
        var venueId = await db.Venues.Select(v => v.Id).SingleAsync();

        var result = await CreateHandler(db).HandleAsync(ValidCreate(venueId));

        result.IsSuccess.Should().BeTrue();
        result.Value.AccountDetailsMasked.Should().Be("•••• 0002");
        var stored = await db.PaymentMethods.SingleAsync();
        Encoding.UTF8.GetString(stored.AccountDetailsCiphertext).Should().NotContain(_iban);
        _cipher.LastContext!.ToAssociatedData().Should().Be($"payment_methods|account_details|{stored.Id:D}");
        _audit.Actions.Should().Equal("PaymentMethod.Created");
    }

    [Fact]
    public async Task Create_ByNonOwner_ReturnsNotFound_AndStoresNothingAsync()
    {
        await using var db = await SeedAsync();
        var venueId = await db.Venues.Select(v => v.Id).SingleAsync();

        var result = await CreateHandler(db).HandleAsync(ValidCreate(venueId) with { UserId = Guid.NewGuid() });

        result.Error.Code.Should().Be("Venue.NotFound");
        (await db.PaymentMethods.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Create_InvalidDetailsOrDeposit_ReturnsValidationErrorsAsync()
    {
        await using var db = await SeedAsync();
        var venueId = await db.Venues.Select(v => v.Id).SingleAsync();
        var handler = CreateHandler(db);

        (await handler.HandleAsync(ValidCreate(venueId) with { AccountDetails = "not-an-account" })).Error.Code
            .Should().Be("PaymentMethod.InvalidAccountDetails");
        (await handler.HandleAsync(ValidCreate(venueId) with { DepositValue = 150 })).Error.Code
            .Should().Be("PaymentMethod.InvalidDeposit");
        (await handler.HandleAsync(ValidCreate(venueId) with { MethodType = "Cash" })).Error.Code
            .Should().Be("PaymentMethod.InvalidType");
    }

    [Fact]
    public async Task List_ReturnsMaskedOnly_AndReveal_DecryptsAndAuditsAsync()
    {
        await using var db = await SeedAsync();
        var venueId = await db.Venues.Select(v => v.Id).SingleAsync();
        var created = (await CreateHandler(db).HandleAsync(ValidCreate(venueId))).Value;
        var access = new VenueAccessService(db);

        var list = await new GetPaymentMethodsHandler(access, db).HandleAsync(new GetPaymentMethodsQuery(venueId, _ownerId));
        var revealed = await new RevealPaymentMethodDetailsHandler(access, _cipher, _audit, db)
            .HandleAsync(new RevealPaymentMethodDetailsQuery(venueId, created.Id, _ownerId));

        list.Value.Should().ContainSingle().Which.AccountDetailsMasked.Should().Be("•••• 0002");
        revealed.Value.AccountDetails.Should().Be(_iban);
        _audit.Actions.Should().Contain("PaymentMethod.DetailsRevealed");
    }

    [Fact]
    public async Task Reveal_ByNonOwner_ReturnsNotFound_WithoutDecryptingAsync()
    {
        await using var db = await SeedAsync();
        var venueId = await db.Venues.Select(v => v.Id).SingleAsync();
        var created = (await CreateHandler(db).HandleAsync(ValidCreate(venueId))).Value;
        _cipher.Decryptions = 0;

        var result = await new RevealPaymentMethodDetailsHandler(new VenueAccessService(db), _cipher, _audit, db)
            .HandleAsync(new RevealPaymentMethodDetailsQuery(venueId, created.Id, Guid.NewGuid()));

        result.Error.Code.Should().Be("Venue.NotFound");
        _cipher.Decryptions.Should().Be(0);
    }

    [Fact]
    public async Task Update_ReEncryptsNewDetails_AndDelete_SoftDeletesAsync()
    {
        await using var db = await SeedAsync();
        var venueId = await db.Venues.Select(v => v.Id).SingleAsync();
        var created = (await CreateHandler(db).HandleAsync(ValidCreate(venueId))).Value;
        var access = new VenueAccessService(db);

        var updated = await new UpdatePaymentMethodHandler(access, _cipher, _audit, db, _clock).HandleAsync(new UpdatePaymentMethodCommand(
            venueId, created.Id, _ownerId, PaymentMethodType.MobileWallet, "Club", "01012345678", null, DepositType.Full, 0, true));
        var deleted = await new DeletePaymentMethodHandler(access, _audit, db, _clock)
            .HandleAsync(new DeletePaymentMethodCommand(venueId, created.Id, _ownerId));

        updated.Value.AccountDetailsMasked.Should().Be("•••• 5678");
        deleted.IsSuccess.Should().BeTrue();
        (await db.PaymentMethods.SingleAsync()).IsDeleted.Should().BeTrue();
        (await new GetPaymentMethodsHandler(access, db).HandleAsync(new GetPaymentMethodsQuery(venueId, _ownerId))).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ReEncryptionTarget_ReEncryptsOnlyStaleValuesAsync()
    {
        await using var db = await SeedAsync();
        var venueId = await db.Venues.Select(v => v.Id).SingleAsync();
        await CreateHandler(db).HandleAsync(ValidCreate(venueId));
        var target = new PaymentMethodReEncryptionTarget(db, _clock);

        _cipher.CurrentKeyVersion = 2;
        var first = await target.ReEncryptBatchAsync(_cipher, 10);
        var second = await target.ReEncryptBatchAsync(_cipher, 10);

        first.Should().Be(1);
        second.Should().Be(0);
    }

    private CreatePaymentMethodHandler CreateHandler(VenuesDbContext db) =>
        new(new VenueAccessService(db), _cipher, _audit, db, _clock);

    private CreatePaymentMethodCommand ValidCreate(Guid venueId) =>
        new(venueId, _ownerId, PaymentMethodType.BankTransfer, "Malabak Club", _iban, "Write booking ref in notes", DepositType.Percentage, 30);

    private async Task<VenuesDbContext> SeedAsync()
    {
        var db = new VenuesDbContext(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Venues.Add(Venue.Create(Guid.NewGuid(), "Club", "club", null, _ownerId, "Addr", "Cairo", "Maadi", null, null, "+201000000001", null, null, _clock.UtcNow));
        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>Reversible stand-in for the real cipher: prefixes a key version byte and records the AAD context.</summary>
    private sealed class FakeCipher : IFieldCipher
    {
        public byte CurrentKeyVersion { get; set; } = 1;
        public FieldEncryptionContext? LastContext { get; private set; }
        public int Decryptions { get; set; }

        public ValueTask<byte[]> EncryptAsync(string plaintext, FieldEncryptionContext context, CancellationToken cancellationToken = default)
        {
            LastContext = context;
            var data = Encoding.UTF8.GetBytes(plaintext).Select(b => (byte)(b ^ 0x5A));
            return ValueTask.FromResult<byte[]>([CurrentKeyVersion, .. data]);
        }

        public ValueTask<string> DecryptAsync(byte[] ciphertext, FieldEncryptionContext context, CancellationToken cancellationToken = default)
        {
            Decryptions++;
            return ValueTask.FromResult(Encoding.UTF8.GetString(ciphertext.Skip(1).Select(b => (byte)(b ^ 0x5A)).ToArray()));
        }

        public ValueTask<bool> NeedsReEncryptionAsync(byte[] ciphertext, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ciphertext[0] != CurrentKeyVersion);
    }

    private sealed class RecordingAuditLogger : IVenueAuditLogger
    {
        public List<string> Actions { get; } = [];

        public Task LogAsync(string action, string entityName, Guid entityId, Guid userId, object? metadata = null, CancellationToken cancellationToken = default)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
