using System.Security.Cryptography;
using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Malabak.Modules.Identity.Infrastructure.Services;

public sealed class JwtKeyProvider : IJwtKeyProvider, IDisposable
{
    private readonly ECDsa _currentEcdsa;
    private readonly ECDsaSecurityKey _currentSigningKey;
    private readonly SigningCredentials _signingCredentials;
    private readonly List<SecurityKey> _validationKeys = [];
    private bool _disposed;

    public JwtKeyProvider(IOptions<JwtOptions> options)
    {
        var opts = options.Value;
        CurrentKeyId = string.IsNullOrWhiteSpace(opts.CurrentKeyId) ? "malabak-es256-key-v1" : opts.CurrentKeyId;

        _currentEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        if (!string.IsNullOrWhiteSpace(opts.PrivateKeyPem))
        {
            _currentEcdsa.ImportFromPem(opts.PrivateKeyPem);
        }

        _currentSigningKey = new ECDsaSecurityKey(_currentEcdsa)
        {
            KeyId = CurrentKeyId
        };

        _signingCredentials = new SigningCredentials(_currentSigningKey, SecurityAlgorithms.EcdsaSha256);

        _validationKeys.Add(_currentSigningKey);
    }

    public string CurrentKeyId { get; }

    public ECDsaSecurityKey GetCurrentSigningKey() => _currentSigningKey;

    public SigningCredentials GetCurrentSigningCredentials() => _signingCredentials;

    public IEnumerable<SecurityKey> GetValidationKeys() => _validationKeys;

    public void RegisterRotatedValidationKey(string keyId, ECDsa ecdsaKey)
    {
        var securityKey = new ECDsaSecurityKey(ecdsaKey)
        {
            KeyId = keyId
        };

        _validationKeys.Add(securityKey);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _currentEcdsa.Dispose();
            _disposed = true;
        }
    }
}
