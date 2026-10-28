using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace Malabak.Api.Infrastructure.Logging;

public sealed partial class PiiMaskingEnricher : ILogEventEnricher
{
    private static readonly HashSet<string> _sensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password",
        "CurrentPassword",
        "NewPassword",
        "AdminPassword",
        "Token",
        "AccessToken",
        "RefreshToken",
        "StepUpToken",
        "TurnstileToken",
        "IdToken",
        "Otp",
        "VerificationCode",
        "Code",
        "Secret",
        "ClientSecret",
        "ApiKey",
        "Authorization",
        "CreditCard",
        "CardNumber",
        "Cvv",
        "Phone",
        "PhoneNumber",
        "Email"
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        foreach (var property in logEvent.Properties.Where(p => IsSensitiveKey(p.Key)).ToList())
        {
            var maskedValue = MaskLogEventPropertyValue(property.Value);
            logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, maskedValue));
        }
    }

    private static bool IsSensitiveKey(string key)
    {
        if (_sensitiveKeys.Contains(key))
        {
            return true;
        }

        return _sensitiveKeys.Any(sensitive => key.Contains(sensitive, StringComparison.OrdinalIgnoreCase));
    }

    private static LogEventPropertyValue MaskLogEventPropertyValue(LogEventPropertyValue value)
    {
        if (value is ScalarValue scalar && scalar.Value is string stringValue)
        {
            return new ScalarValue(MaskSensitiveString(stringValue));
        }

        if (value is StructureValue structure)
        {
            var maskedProperties = structure.Properties.Select(p =>
                IsSensitiveKey(p.Name)
                    ? new LogEventProperty(p.Name, MaskLogEventPropertyValue(p.Value))
                    : p);

            return new StructureValue(maskedProperties, structure.TypeTag);
        }

        if (value is DictionaryValue dictionary)
        {
            var maskedElements = dictionary.Elements.Select(kv =>
            {
                var keyStr = kv.Key.Value?.ToString() ?? string.Empty;
                var val = IsSensitiveKey(keyStr) ? MaskLogEventPropertyValue(kv.Value) : kv.Value;
                return new KeyValuePair<ScalarValue, LogEventPropertyValue>(kv.Key, val);
            });

            return new DictionaryValue(maskedElements);
        }

        return new ScalarValue("***MASKED***");
    }

    private static string MaskSensitiveString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "***MASKED***";
        }

        // Check if phone number
        if (EgyptianPhoneRegex().IsMatch(raw))
        {
            var digitsOnly = raw.Replace(" ", string.Empty, StringComparison.Ordinal)
                                .Replace("-", string.Empty, StringComparison.Ordinal);
            if (digitsOnly.Length >= 8)
            {
                return $"{digitsOnly[..4]}****{digitsOnly[^4..]}";
            }
        }

        // Check if email
        if (EmailRegex().IsMatch(raw))
        {
            var atIndex = raw.IndexOf('@', StringComparison.Ordinal);
            if (atIndex > 1)
            {
                return $"{raw[0]}***{raw[atIndex..]}";
            }
        }

        return "***MASKED***";
    }

    [GeneratedRegex(@"^(\+?20|0)?1[0125]\d{8}$")]
    private static partial Regex EgyptianPhoneRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
