using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.PaymentMethods;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class VenuePaymentDirectoryTests
{
    private static readonly DateTime _now = new(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AMethodThatCannotBeDecrypted_IsLeftOut_AndTheOthersStillShowAsync()
    {
        await using var db = new VenuesDbContext(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var venueId = Guid.NewGuid();
        var readable = Method(venueId, "01012345678", _now);
        var unreadable = Method(venueId, "broken", _now.AddMinutes(1));
        db.PaymentMethods.AddRange(readable, unreadable);
        await db.SaveChangesAsync();
        var audit = new Audit();

        var methods = await new VenuePaymentDirectory(db, new Cipher(), audit).RevealForBookingAsync(venueId, Guid.NewGuid(), Guid.NewGuid());

        methods.Should().ContainSingle().Which.AccountDetails.Should().Be("01012345678");
        audit.Count.Should().Be(1, "only details actually shown are audited");
    }

    private static VenuePaymentMethod Method(Guid venueId, string details, DateTime createdAt)
    {
        var method = VenuePaymentMethod.Create(Guid.NewGuid(), venueId, PaymentMethodType.MobileWallet, "Owner", null, DepositRule.Create(DepositType.FixedAmount, 50), createdAt);
        method.SetAccountDetails(Encoding.UTF8.GetBytes(details), details, createdAt);
        return method;
    }

    /// <summary>"Decrypts" by reading the bytes back, and fails like a missing key for the value "broken".</summary>
    private sealed class Cipher : IFieldCipher
    {
        public ValueTask<byte[]> EncryptAsync(string plaintext, FieldEncryptionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Encoding.UTF8.GetBytes(plaintext));

        public ValueTask<string> DecryptAsync(byte[] ciphertext, FieldEncryptionContext context, CancellationToken cancellationToken = default)
        {
            var text = Encoding.UTF8.GetString(ciphertext);
            return text == "broken"
                ? throw new CryptographicException("The data encryption key for this value is unknown.")
                : ValueTask.FromResult(text);
        }

        public ValueTask<bool> NeedsReEncryptionAsync(byte[] ciphertext, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class Audit : IVenueAuditLogger
    {
        public int Count { get; private set; }

        public Task LogAsync(string action, string entityName, Guid entityId, Guid userId, object? metadata = null, CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }
}
