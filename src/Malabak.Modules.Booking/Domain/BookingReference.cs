using System.Security.Cryptography;

namespace Malabak.Modules.Booking.Domain;

/// <summary>
/// The public, human-friendly booking reference shown to players and venues, for example <c>MLB-4F7K2Q</c>
/// (rule.md section 5). It is random, so it never reveals how many bookings exist, and it is read aloud or typed by
/// people, so the alphabet leaves out look-alike characters (<c>0 O 1 I L U</c>). Uniqueness is guaranteed by the unique
/// index on the column, not by the generator: callers retry when an insert collides.
/// </summary>
public static class BookingReference
{
    public const string Prefix = "MLB-";
    public const int CodeLength = 6;

    /// <summary>30 symbols: digits 2-9 and the letters except I, L, O and U.</summary>
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Generate()
    {
        Span<char> code = stackalloc char[CodeLength];
        for (var i = 0; i < code.Length; i++)
        {
            // GetInt32 is unbiased, unlike taking a random byte modulo the alphabet size.
            code[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return string.Concat(Prefix, code);
    }

    /// <summary>Whether the value has the shape of a reference this generator can produce (case-sensitive).</summary>
    public static bool IsValid(string? value)
    {
        if (value is null || value.Length != Prefix.Length + CodeLength || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var c in value.AsSpan(Prefix.Length))
        {
            if (!Alphabet.Contains(c, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
