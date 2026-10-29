using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Malabak.Modules.Identity.Application.Common;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>RFC 6238 TOTP (HMAC-SHA1, 30-second steps, 6 digits), accepting ±1 step for clock drift.</summary>
public sealed class TotpService : ITotpService
{
    private const int _stepSeconds = 30;
    private const int _digits = 6;
    private const string _base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public string GenerateSecret() => ToBase32(RandomNumberGenerator.GetBytes(20));

    public bool VerifyCode(string base32Secret, string code, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != _digits || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        var key = FromBase32(base32Secret);
        var step = new DateTimeOffset(nowUtc).ToUnixTimeSeconds() / _stepSeconds;
        var expected = Encoding.ASCII.GetBytes(code);
        var matched = false;

        for (var offset = -1; offset <= 1; offset++)
        {
            // Compare every window without short-circuiting to keep timing uniform.
            matched |= CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(key, step + offset)), expected);
        }

        return matched;
    }

    public string BuildProvisioningUri(string base32Secret, string accountName) =>
        $"otpauth://totp/Malabak:{Uri.EscapeDataString(accountName)}?secret={base32Secret}&issuer=Malabak&digits={_digits}&period={_stepSeconds}";

    internal static string Compute(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);

        // HMAC-SHA1 is mandated by RFC 6238 / authenticator apps; it is not used as a general-purpose hash here.
#pragma warning disable CA5350
        var hash = HMACSHA1.HashData(key, counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset)) & 0x7FFFFFFF;
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static string ToBase32(byte[] data)
    {
        var output = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(_base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            output.Append(_base32Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return output.ToString();
    }

    internal static byte[] FromBase32(string base32)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in base32.TrimEnd('=').ToUpperInvariant())
        {
            var value = _base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("Invalid base32 secret.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. bytes];
    }
}
