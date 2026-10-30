using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Malabak.Api.Infrastructure.Security;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure.Security;

public sealed class FieldCipherTests : IDisposable
{
    private const string _kekId = "kek-1";

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly string _kek = EncryptionTestHost.NewKey();
    private readonly EncryptionTestHost _host;
    private readonly FieldEncryptionContext _context = new("payment_methods", "account_number", Guid.NewGuid());

    public FieldCipherTests()
    {
        _host = new EncryptionTestHost(_databaseName, EncryptionTestHost.OptionsWith(_kekId, (_kekId, _kek)), new MutableClock());
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task EncryptDecrypt_ArabicAndLatinText_RoundTripsAsync()
    {
        const string plaintext = "EG380019000500000000263180002 - حساب بنك مصر";

        var ciphertext = await _host.Cipher.EncryptAsync(plaintext, _context);
        var decrypted = await _host.Cipher.DecryptAsync(ciphertext, _context);

        decrypted.Should().Be(plaintext);
        ciphertext[0].Should().Be(AesGcmFieldCipher.FormatVersion);
    }

    [Fact]
    public async Task Encrypt_SamePlaintextTwice_UsesFreshNonceEachTimeAsync()
    {
        var first = await _host.Cipher.EncryptAsync("01001234567", _context);
        var second = await _host.Cipher.EncryptAsync("01001234567", _context);

        first.Should().NotEqual(second);
        first.AsSpan(5, AesGcmSealer.NonceSize).ToArray().Should().NotEqual(second.AsSpan(5, AesGcmSealer.NonceSize).ToArray());
    }

    [Fact]
    public async Task Encrypt_Output_DoesNotContainPlaintextAsync()
    {
        const string plaintext = "SECRET-IBAN-1234567890";

        var ciphertext = await _host.Cipher.EncryptAsync(plaintext, _context);

        Encoding.UTF8.GetString(ciphertext).Should().NotContain(plaintext);
    }

    [Fact]
    public async Task Decrypt_CiphertextMovedToAnotherRow_ThrowsAsync()
    {
        var ciphertext = await _host.Cipher.EncryptAsync("secret", _context);
        var otherRow = new FieldEncryptionContext(_context.Table, _context.Column, Guid.NewGuid());

        var act = async () => await _host.Cipher.DecryptAsync(ciphertext, otherRow);

        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task Decrypt_CiphertextMovedToAnotherColumn_ThrowsAsync()
    {
        var ciphertext = await _host.Cipher.EncryptAsync("secret", _context);
        var otherColumn = new FieldEncryptionContext(_context.Table, "wallet_number", _context.RowId);

        var act = async () => await _host.Cipher.DecryptAsync(ciphertext, otherColumn);

        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task Decrypt_TamperedCiphertextByte_ThrowsAsync()
    {
        var ciphertext = await _host.Cipher.EncryptAsync("secret", _context);
        ciphertext[^1] ^= 0x01;

        var act = async () => await _host.Cipher.DecryptAsync(ciphertext, _context);

        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task Decrypt_UnsupportedVersionOrTruncated_ThrowsAsync()
    {
        var ciphertext = await _host.Cipher.EncryptAsync("secret", _context);
        var wrongVersion = ciphertext.ToArray();
        wrongVersion[0] = 99;

        var badVersion = async () => await _host.Cipher.DecryptAsync(wrongVersion, _context);
        var truncated = async () => await _host.Cipher.DecryptAsync(ciphertext[..10], _context);

        await badVersion.Should().ThrowAsync<CryptographicException>();
        await truncated.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task Decrypt_UnknownDataKeyId_ThrowsAsync()
    {
        var ciphertext = await _host.Cipher.EncryptAsync("secret", _context);
        ciphertext[4] = 0x7F; // key id 127, never created

        var act = async () => await _host.Cipher.DecryptAsync(ciphertext, _context);

        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task FirstUse_CreatesSingleActiveDataKey_StoredOnlyWrappedAsync()
    {
        await _host.Cipher.EncryptAsync("secret", _context);

        await using var db = _host.CreateDbContext();
        var keys = await db.DataEncryptionKeys.ToListAsync();
        keys.Should().ContainSingle(k => k.IsActive);
        keys[0].KeyEncryptionKeyId.Should().Be(_kekId);

        var current = await _host.KeyRing.GetCurrentAsync();
        keys[0].WrappedKey.Should().NotEqual(current.Key);
        keys[0].WrappedKey.Length.Should().BeGreaterThan(current.Key.Length);
    }

    [Fact]
    public async Task Rotate_OldCiphertextStillDecrypts_AndIsFlaggedForReEncryptionAsync()
    {
        var before = await _host.Cipher.EncryptAsync("old value", _context);

        var newKeyId = await _host.KeyRing.RotateAsync();
        var after = await _host.Cipher.EncryptAsync("new value", _context);

        (await _host.Cipher.DecryptAsync(before, _context)).Should().Be("old value");
        (await _host.Cipher.DecryptAsync(after, _context)).Should().Be("new value");
        (await _host.Cipher.NeedsReEncryptionAsync(before)).Should().BeTrue();
        (await _host.Cipher.NeedsReEncryptionAsync(after)).Should().BeFalse();
        (await _host.KeyRing.GetCurrentAsync()).Id.Should().Be(newKeyId);

        await using var db = _host.CreateDbContext();
        (await db.DataEncryptionKeys.CountAsync(k => k.IsActive)).Should().Be(1);
        (await db.DataEncryptionKeys.CountAsync(k => !k.IsActive && k.RetiredAtUtc != null)).Should().Be(1);
    }

    [Fact]
    public async Task KeyUnderMissingKek_OnlyItsValuesAreUnreadable_NewValuesStillEncryptAsync()
    {
        var lost = await _host.Cipher.EncryptAsync("under the old KEK", _context);

        // A node that no longer has kek-1 (e.g. it was never persisted) retires the unreadable key.
        const string newKekId = "kek-2";
        using var node = new EncryptionTestHost(_databaseName, EncryptionTestHost.OptionsWith(newKekId, (newKekId, EncryptionTestHost.NewKey())), _host.Clock);
        await node.KeyRing.RotateAsync();

        var fresh = await node.Cipher.EncryptAsync("new value", _context);

        (await node.Cipher.DecryptAsync(fresh, _context)).Should().Be("new value");
        var readLost = async () => await node.Cipher.DecryptAsync(lost, _context);
        await readLost.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task RotationOnAnotherNode_IsPickedUpAfterRefreshIntervalAsync()
    {
        var clock = _host.Clock;
        await _host.Cipher.EncryptAsync("warm up", _context);
        var originalKeyId = (await _host.KeyRing.GetCurrentAsync()).Id;

        using var otherNode = new EncryptionTestHost(_databaseName, _host.Options, clock);
        var rotatedKeyId = await otherNode.KeyRing.RotateAsync();

        (await _host.KeyRing.GetCurrentAsync()).Id.Should().Be(originalKeyId);

        clock.UtcNow = clock.UtcNow.AddMinutes(_host.Options.KeyRingRefreshMinutes);

        (await _host.KeyRing.GetCurrentAsync()).Id.Should().Be(rotatedKeyId);
    }

    [Fact]
    public async Task RewrapAfterKekChange_DataReadableWithOnlyTheNewKekAsync()
    {
        var ciphertext = await _host.Cipher.EncryptAsync("survives KEK rotation", _context);

        const string newKekId = "kek-2";
        var newKek = EncryptionTestHost.NewKey();
        var transition = EncryptionTestHost.OptionsWith(newKekId, (_kekId, _kek), (newKekId, newKek));
        using (var transitionNode = new EncryptionTestHost(_databaseName, transition, _host.Clock))
        {
            (await transitionNode.KeyRing.RewrapWithCurrentKeyEncryptionKeyAsync()).Should().Be(1);
        }

        var afterRetirement = EncryptionTestHost.OptionsWith(newKekId, (newKekId, newKek));
        using var newNode = new EncryptionTestHost(_databaseName, afterRetirement, _host.Clock);

        (await newNode.Cipher.DecryptAsync(ciphertext, _context)).Should().Be("survives KEK rotation");
        await using var db = newNode.CreateDbContext();
        (await db.DataEncryptionKeys.AllAsync(k => k.KeyEncryptionKeyId == newKekId)).Should().BeTrue();
    }

    [Fact]
    public async Task UnwrapWithMissingKek_ThrowsAsync()
    {
        var ciphertext = await _host.Cipher.EncryptAsync("secret", _context);
        var unrelated = EncryptionTestHost.OptionsWith("other", ("other", EncryptionTestHost.NewKey()));
        using var node = new EncryptionTestHost(_databaseName, unrelated, _host.Clock);

        var act = async () => await node.Cipher.DecryptAsync(ciphertext, _context);

        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public void FieldEncryptionContext_RequiresRowId_AndNormalizesNames()
    {
        var act = () => new FieldEncryptionContext("t", "c", Guid.Empty);
        act.Should().Throw<ArgumentException>();

        var id = Guid.NewGuid();
        new FieldEncryptionContext(" Payment_Methods ", "IBAN", id).ToAssociatedData()
            .Should().Be($"payment_methods|iban|{id:D}");
    }
}
