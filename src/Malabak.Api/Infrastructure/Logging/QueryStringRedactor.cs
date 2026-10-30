using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Malabak.Api.Infrastructure.Logging;

public static partial class QueryStringRedactor
{
    private static readonly HashSet<string> _sensitiveQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "token",
        "access_token",
        "refresh_token",
        "id_token",
        "step_up_token",
        "turnstile_token",
        "otp",
        "code",
        "verification_code",
        "password",
        "current_password",
        "new_password",
        "secret",
        "api_key",
        "apikey",
        "key",
        "phone",
        "phone_number",
        "phonenumber",
        "mobile",
        "email",
        "sig",
        "signature",
        "auth",
        "authorization"
    };

    // Player location (explore and map): logged rounded to 2 decimals (about 1 km), never precisely.
    private static readonly HashSet<string> _coordinateQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "lat", "lng", "lon", "latitude", "longitude", "south", "west", "north", "east"
    };

    public static string RedactQueryString(QueryString queryString)
    {
        if (!queryString.HasValue || string.IsNullOrWhiteSpace(queryString.Value))
        {
            return string.Empty;
        }

        var parsed = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(queryString.Value);
        if (parsed.Count == 0)
        {
            return string.Empty;
        }

        var sanitizedParameters = new List<string>(parsed.Count);

        foreach (var (key, values) in parsed)
        {
            if (IsSensitiveQueryKey(key))
            {
                sanitizedParameters.Add($"{Uri.EscapeDataString(key)}=***MASKED***");
            }
            else if (_coordinateQueryKeys.Contains(key))
            {
                sanitizedParameters.Add($"{Uri.EscapeDataString(key)}={string.Join(",", values.Select(RoundCoordinate))}");
            }
            else
            {
                var sanitizedValues = values.Select(v =>
                    string.IsNullOrWhiteSpace(v) ? string.Empty : MaskPiiString(v));
                sanitizedParameters.Add($"{Uri.EscapeDataString(key)}={string.Join(",", sanitizedValues)}");
            }
        }

        return "?" + string.Join("&", sanitizedParameters);
    }

    private static bool IsSensitiveQueryKey(string key)
    {
        if (_sensitiveQueryKeys.Contains(key))
        {
            return true;
        }

        return _sensitiveQueryKeys.Any(s => key.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    public static string RoundCoordinate(string? raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? Math.Round(value, 2).ToString("0.00", CultureInfo.InvariantCulture)
            : "***MASKED***";

    private static string MaskPiiString(string raw)
    {
        if (EgyptianPhoneRegex().IsMatch(raw))
        {
            var digitsOnly = raw.Replace(" ", string.Empty, StringComparison.Ordinal)
                                .Replace("-", string.Empty, StringComparison.Ordinal);
            if (digitsOnly.Length >= 8)
            {
                return $"{digitsOnly[..4]}****{digitsOnly[^4..]}";
            }
        }

        if (EmailRegex().IsMatch(raw))
        {
            var atIndex = raw.IndexOf('@', StringComparison.Ordinal);
            if (atIndex > 1)
            {
                return $"{raw[0]}***{raw[atIndex..]}";
            }
        }

        return raw;
    }

    [GeneratedRegex(@"^(\+?20|0)?1[0125]\d{8}$")]
    private static partial Regex EgyptianPhoneRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
