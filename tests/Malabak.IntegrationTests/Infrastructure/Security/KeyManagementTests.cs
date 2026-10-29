using System.Security.Cryptography;
using System.Xml.Linq;
using FluentAssertions;
using Malabak.Api.Infrastructure.Security;
using Malabak.SharedKernel.Security;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure.Security;

public sealed class KeyManagementTests
{
    [Fact]
    public void BlindIndex_SameInput_IsDeterministic256BitHash()
    {
        var service = new HmacBlindIndexService(Options.Create(EncryptionTestHost.OptionsWith("k", ("k", EncryptionTestHost.NewKey()))));

        var first = service.Compute("+201001234567", BlindIndexPurpose.Phone);
        var second = service.Compute("+201001234567", BlindIndexPurpose.Phone);

        first.Should().Equal(second);
        first.Should().HaveCount(32);
    }

    [Fact]
    public void BlindIndex_DifferentPurposeOrKey_ProducesUnrelatedIndexes()
    {
        var options = EncryptionTestHost.OptionsWith("k", ("k", EncryptionTestHost.NewKey()));
        var service = new HmacBlindIndexService(Options.Create(options));
        var otherKeyService = new HmacBlindIndexService(Options.Create(EncryptionTestHost.OptionsWith("k", ("k", EncryptionTestHost.NewKey()))));

        var phone = service.Compute("user@example.com", BlindIndexPurpose.Phone);
        var email = service.Compute("user@example.com", BlindIndexPurpose.Email);
        var otherKey = otherKeyService.Compute("user@example.com", BlindIndexPurpose.Email);

        phone.Should().NotEqual(email);
        email.Should().NotEqual(otherKey);
    }

    [Fact]
    public void BlindIndex_PurposeWithSeparator_Throws()
    {
        var service = new HmacBlindIndexService(Options.Create(EncryptionTestHost.OptionsWith("k", ("k", EncryptionTestHost.NewKey()))));

        var act = () => service.Compute("value", "phone|email");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validator_ValidOptions_Succeeds()
    {
        var options = EncryptionTestHost.OptionsWith("k1", ("k1", EncryptionTestHost.NewKey()));

        new EncryptionOptionsValidator().Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validator_CurrentKekNotConfigured_Fails()
    {
        var options = EncryptionTestHost.OptionsWith("missing", ("k1", EncryptionTestHost.NewKey()));

        new EncryptionOptionsValidator().Validate(null, options).Failed.Should().BeTrue();
    }

    [Fact]
    public void Validator_KeyOfWrongLength_Fails()
    {
        var options = EncryptionTestHost.OptionsWith("k1", ("k1", Convert.ToBase64String(new byte[16])));

        new EncryptionOptionsValidator().Validate(null, options).Failed.Should().BeTrue();
    }

    [Fact]
    public void Validator_BlindIndexKeyReusesKek_Fails()
    {
        var shared = EncryptionTestHost.NewKey();
        var options = EncryptionTestHost.OptionsWith("k1", ("k1", shared));
        options.BlindIndexKey = shared;

        new EncryptionOptionsValidator().Validate(null, options).Failed.Should().BeTrue();
    }

    [Fact]
    public void Setup_InDevelopmentWithoutKeys_GeneratesEphemeralKeys()
    {
        var options = new EncryptionOptions();

        new EncryptionOptionsSetup(new TestHostEnvironment { EnvironmentName = Environments.Development }).PostConfigure(null, options);

        options.UsesEphemeralDevelopmentKeys.Should().BeTrue();
        new EncryptionOptionsValidator().Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Setup_InProductionWithoutKeys_LeavesThemMissingSoStartupFails()
    {
        var options = new EncryptionOptions();

        new EncryptionOptionsSetup(new TestHostEnvironment { EnvironmentName = Environments.Production }).PostConfigure(null, options);

        options.UsesEphemeralDevelopmentKeys.Should().BeFalse();
        new EncryptionOptionsValidator().Validate(null, options).Failed.Should().BeTrue();
    }

    [Fact]
    public void XmlEncryptor_RoundTrips_WithoutExposingKeyMaterial()
    {
        var kek = new LocalKeyEncryptionKeyProvider(
            Options.Create(EncryptionTestHost.OptionsWith("k1", ("k1", EncryptionTestHost.NewKey()))),
            NullLogger<LocalKeyEncryptionKeyProvider>.Instance);
        var original = new XElement("key", new XElement("masterKey", "TOP-SECRET-MASTER-KEY"));

        var encrypted = new KeyEncryptionKeyXmlEncryptor(kek).Encrypt(original);
        var decrypted = new KeyEncryptionKeyXmlDecryptor(kek).Decrypt(encrypted.EncryptedElement);

        encrypted.EncryptedElement.ToString().Should().NotContain("TOP-SECRET-MASTER-KEY");
        encrypted.DecryptorType.Should().Be<KeyEncryptionKeyXmlDecryptor>();
        XNode.DeepEquals(original, decrypted).Should().BeTrue();
    }

    [Fact]
    public void XmlDecryptor_WithDifferentKek_Throws()
    {
        var writer = new LocalKeyEncryptionKeyProvider(
            Options.Create(EncryptionTestHost.OptionsWith("k1", ("k1", EncryptionTestHost.NewKey()))),
            NullLogger<LocalKeyEncryptionKeyProvider>.Instance);
        var reader = new LocalKeyEncryptionKeyProvider(
            Options.Create(EncryptionTestHost.OptionsWith("k1", ("k1", EncryptionTestHost.NewKey()))),
            NullLogger<LocalKeyEncryptionKeyProvider>.Instance);

        var encrypted = new KeyEncryptionKeyXmlEncryptor(writer).Encrypt(new XElement("key", "value"));
        var act = () => new KeyEncryptionKeyXmlDecryptor(reader).Decrypt(encrypted.EncryptedElement);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void XmlRepository_StoresAndReturnsElements()
    {
        using var host = new EncryptionTestHost(
            Guid.NewGuid().ToString(),
            EncryptionTestHost.OptionsWith("k1", ("k1", EncryptionTestHost.NewKey())),
            new MutableClock());
        var repository = new SecurityDbXmlRepository(host.ScopeFactory, host.Clock);

        repository.StoreElement(new XElement("key", new XAttribute("id", "1")), "key-1");
        repository.StoreElement(new XElement("key", new XAttribute("id", "2")), "key-2");

        repository.GetAllElements().Select(e => e.Attribute("id")!.Value).Should().Equal("1", "2");
    }

    [Fact]
    public async Task RotationService_ReEncryptAll_DrainsEveryTargetAsync()
    {
        using var host = new EncryptionTestHost(
            Guid.NewGuid().ToString(),
            EncryptionTestHost.OptionsWith("k1", ("k1", EncryptionTestHost.NewKey())),
            new MutableClock());
        var target = new CountingTarget(remaining: 450);
        var service = new EncryptionKeyRotationService(
            host.KeyRing, host.Cipher, [target], NullLogger<EncryptionKeyRotationService>.Instance);

        var total = await service.ReEncryptAllAsync(batchSize: 200);

        total.Should().Be(450);
        target.Calls.Should().Be(4); // 200 + 200 + 50 + final empty batch
    }

    [Fact]
    public async Task RotationService_RotateDataKey_ActivatesNewKeyAsync()
    {
        using var host = new EncryptionTestHost(
            Guid.NewGuid().ToString(),
            EncryptionTestHost.OptionsWith("k1", ("k1", EncryptionTestHost.NewKey())),
            new MutableClock());
        var before = (await host.KeyRing.GetCurrentAsync()).Id;
        var service = new EncryptionKeyRotationService(
            host.KeyRing, host.Cipher, [], NullLogger<EncryptionKeyRotationService>.Instance);

        await service.RotateDataKeyAsync();

        (await host.KeyRing.GetCurrentAsync()).Id.Should().NotBe(before);
    }

    private sealed class CountingTarget : IReEncryptionTarget
    {
        private int _remaining;

        public CountingTarget(int remaining) => _remaining = remaining;

        public string Name => "counting";
        public int Calls { get; private set; }

        public Task<int> ReEncryptBatchAsync(IFieldCipher cipher, int batchSize, CancellationToken cancellationToken = default)
        {
            Calls++;
            var processed = Math.Min(batchSize, _remaining);
            _remaining -= processed;
            return Task.FromResult(processed);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Malabak.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
