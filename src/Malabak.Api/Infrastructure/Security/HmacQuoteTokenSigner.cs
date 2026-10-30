using System.Security.Cryptography;
using System.Text;
using Malabak.Modules.Venues.Application.Quotes;

namespace Malabak.Api.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256 quote tokens: base64url(payload) "." base64url(mac). The payload is not secret (it only holds the slot and
/// the agreed amounts) but cannot be altered. Key from <c>Booking:QuoteSigningKey</c> (secret store); Development gets a
/// per-process random key, every other environment refuses to start without one.
/// </summary>
public sealed class HmacQuoteTokenSigner : IQuoteTokenSigner
{
    private readonly byte[] _key;

    public HmacQuoteTokenSigner(IConfiguration configuration, IHostEnvironment environment)
    {
        var key = configuration["Booking:QuoteSigningKey"];
        if (!string.IsNullOrWhiteSpace(key))
        {
            _key = Encoding.UTF8.GetBytes(key);
        }
        else if (environment.IsDevelopment())
        {
            _key = RandomNumberGenerator.GetBytes(32);
        }
        else
        {
            throw new InvalidOperationException("Required secret 'Booking:QuoteSigningKey' is not configured.");
        }
    }

    public string Protect(byte[] payload) => $"{Base64Url(payload)}.{Base64Url(HMACSHA256.HashData(_key, payload))}";

    public byte[]? Unprotect(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 2)
        {
            return null;
        }

        var payload = FromBase64Url(parts[0]);
        var mac = FromBase64Url(parts[1]);
        if (payload is null || mac is null)
        {
            return null;
        }

        return CryptographicOperations.FixedTimeEquals(mac, HMACSHA256.HashData(_key, payload)) ? payload : null;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
