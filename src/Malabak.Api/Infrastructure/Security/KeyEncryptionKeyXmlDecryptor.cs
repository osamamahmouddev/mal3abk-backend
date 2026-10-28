using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace Malabak.Api.Infrastructure.Security;

public sealed class KeyEncryptionKeyXmlDecryptor : IXmlDecryptor
{
    private readonly IKeyEncryptionKeyProvider _kekProvider;

    // Data Protection activates decryptors by type with an IServiceProvider constructor.
    public KeyEncryptionKeyXmlDecryptor(IServiceProvider services)
        : this(services.GetRequiredService<IKeyEncryptionKeyProvider>())
    {
    }

    public KeyEncryptionKeyXmlDecryptor(IKeyEncryptionKeyProvider kekProvider)
    {
        _kekProvider = kekProvider;
    }

    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        var kekId = encryptedElement.Attribute(KeyEncryptionKeyXmlEncryptor.KekIdAttribute)?.Value
            ?? throw new CryptographicException("Encrypted key element has no KEK id.");
        var wrappedKey = Convert.FromBase64String(ReadChild(encryptedElement, KeyEncryptionKeyXmlEncryptor.WrappedKeyElement));
        var payload = Convert.FromBase64String(ReadChild(encryptedElement, KeyEncryptionKeyXmlEncryptor.PayloadElement));

        var elementKey = _kekProvider.UnwrapKey(kekId, wrappedKey);
        try
        {
            var xml = AesGcmSealer.Open(elementKey, payload, KeyEncryptionKeyXmlEncryptor.AssociatedData);
            return XElement.Parse(Encoding.UTF8.GetString(xml));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(elementKey);
        }
    }

    private static string ReadChild(XElement element, string name) =>
        element.Element(name)?.Value ?? throw new CryptographicException($"Encrypted key element is missing '{name}'.");
}
