using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace Malabak.Api.Infrastructure.Security;

/// <summary>
/// Encrypts Data Protection key XML at rest using envelope encryption: a fresh per-element key seals the XML
/// and is itself wrapped by the current KEK, so database access alone never yields usable key material.
/// </summary>
public sealed class KeyEncryptionKeyXmlEncryptor : IXmlEncryptor
{
    internal const string ElementName = "kekEncryptedKey";
    internal const string KekIdAttribute = "kekId";
    internal const string WrappedKeyElement = "wrappedKey";
    internal const string PayloadElement = "payload";
    internal static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("malabak-dataprotection");

    private readonly IKeyEncryptionKeyProvider _kekProvider;

    public KeyEncryptionKeyXmlEncryptor(IKeyEncryptionKeyProvider kekProvider)
    {
        _kekProvider = kekProvider;
    }

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        var elementKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(EncryptionOptions.KeySizeBytes);
        try
        {
            var payload = AesGcmSealer.Seal(
                elementKey,
                Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting)),
                AssociatedData);

            var encrypted = new XElement(
                ElementName,
                new XAttribute(KekIdAttribute, _kekProvider.CurrentKeyId),
                new XElement(WrappedKeyElement, Convert.ToBase64String(_kekProvider.WrapKey(elementKey))),
                new XElement(PayloadElement, Convert.ToBase64String(payload)));

            return new EncryptedXmlInfo(encrypted, typeof(KeyEncryptionKeyXmlDecryptor));
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(elementKey);
        }
    }
}
