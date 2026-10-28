using System.Text.RegularExpressions;

namespace Malabak.SharedKernel;

public sealed partial record PhoneNumber
{
    public string Value { get; init; }

    public PhoneNumber(string value)
    {
        var result = Create(value);
        if (result.IsFailure)
        {
            throw new ArgumentException(result.Error.Description, nameof(value));
        }

        Value = result.Value.Value;
    }

    private PhoneNumber()
    {
        Value = string.Empty;
    }

    public bool IsEgyptian => Value.StartsWith("+20", StringComparison.Ordinal);

    public static Result<PhoneNumber> Create(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return Result.Failure<PhoneNumber>(PhoneNumberErrors.Empty);
        }

        var normalized = Normalize(rawValue);

        if (normalized.StartsWith("+20", StringComparison.Ordinal))
        {
            if (!EgyptianMobileRegex().IsMatch(normalized))
            {
                return Result.Failure<PhoneNumber>(PhoneNumberErrors.InvalidEgyptianNumber);
            }
        }
        else
        {
            if (!GeneralE164Regex().IsMatch(normalized))
            {
                return Result.Failure<PhoneNumber>(PhoneNumberErrors.InvalidFormat);
            }
        }

        return Result.Success(new PhoneNumber { Value = normalized });
    }

    public static PhoneNumber From(string rawValue) => new(rawValue);

    public string ToMaskedString()
    {
        if (Value.Length < 8)
        {
            return "****";
        }

        var prefixLength = IsEgyptian ? 5 : 4;
        var suffixLength = 4;
        var maskLength = Math.Max(2, Value.Length - prefixLength - suffixLength);

        var prefix = Value[..prefixLength];
        var suffix = Value[^suffixLength..];
        var mask = new string('*', maskLength);

        return $"{prefix}{mask}{suffix}";
    }

    public override string ToString() => Value;

    public static implicit operator string(PhoneNumber phone) => phone?.Value ?? string.Empty;

    private static string Normalize(string input)
    {
        var cleaned = input.Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal);

        if (cleaned.StartsWith("00", StringComparison.Ordinal))
        {
            cleaned = "+" + cleaned[2..];
        }

        // Egyptian local format (starts with 01): normalize to +201...
        if (cleaned.StartsWith("01", StringComparison.Ordinal) && cleaned.All(char.IsAsciiDigit))
        {
            return "+2" + cleaned;
        }

        // Egyptian with country code without plus: 201...
        if (cleaned.StartsWith("201", StringComparison.Ordinal) && cleaned.All(char.IsAsciiDigit))
        {
            return "+" + cleaned;
        }

        if (!cleaned.StartsWith("+", StringComparison.Ordinal) && cleaned.Length >= 7 && cleaned.All(char.IsAsciiDigit))
        {
            return "+" + cleaned;
        }

        return cleaned;
    }

    [GeneratedRegex(@"^\+201[0125]\d{8}$")]
    private static partial Regex EgyptianMobileRegex();

    [GeneratedRegex(@"^\+[1-9]\d{6,14}$")]
    private static partial Regex GeneralE164Regex();
}
