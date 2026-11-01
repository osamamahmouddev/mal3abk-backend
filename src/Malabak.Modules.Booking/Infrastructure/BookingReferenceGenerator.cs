using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Malabak.Modules.Booking.Domain;

namespace Malabak.Modules.Booking.Infrastructure;

public sealed partial class BookingReferenceGenerator : IBookingReferenceGenerator
{
    private const string _prefix = "MLB-";
    private const string _alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"; // 32 chars, excludes 0, 1, I, O
    private const int _codeLength = 6;

    [GeneratedRegex(@"^MLB-[2-9A-HJ-NP-Z]{6}$")]
    private static partial Regex ReferenceRegex();

    public string Generate()
    {
        Span<char> buffer = stackalloc char[_codeLength];
        for (var i = 0; i < _codeLength; i++)
        {
            var idx = RandomNumberGenerator.GetInt32(_alphabet.Length);
            buffer[i] = _alphabet[idx];
        }

        return $"{_prefix}{buffer}";
    }

    public bool IsValid(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.Length != Prefix.Length + CodeLength)
        {
            return false;
        }

        return ReferenceRegex().IsMatch(reference);
    }
}
