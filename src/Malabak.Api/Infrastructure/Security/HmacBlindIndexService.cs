using System.Security.Cryptography;
using System.Text;
using Malabak.SharedKernel.Security;
using Microsoft.Extensions.Options;

namespace Malabak.Api.Infrastructure.Security;

public sealed class HmacBlindIndexService : IBlindIndexService
{
    private readonly byte[] _key;

    public HmacBlindIndexService(IOptions<EncryptionOptions> options)
    {
        _key = Convert.FromBase64String(options.Value.BlindIndexKey);
    }

    public byte[] Compute(string normalizedValue, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        if (purpose.Contains('|', StringComparison.Ordinal))
        {
            throw new ArgumentException("Purpose must not contain '|'.", nameof(purpose));
        }

        // "purpose|value": the separator can't occur in a purpose label, so distinct pairs never collide.
        return HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{purpose}|{normalizedValue}"));
    }
}
