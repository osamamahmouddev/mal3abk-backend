using System.Text.RegularExpressions;

namespace Malabak.SharedKernel.Caching;

/// <summary>
/// Enforces Redis key conventions across the Malabak modular monolith.
/// Standard format: {prefix}:{environment}:{module}:{category}:{identifier}
/// </summary>
public sealed class RedisKeyConventions : IRedisKeyBuilder
{
    public const string DefaultPrefix = "malabak";
    private static readonly Regex _invalidCharRegex = new(@"[\s\r\n\t]", RegexOptions.Compiled);

    public string Prefix { get; }
    public string Environment { get; }

    public RedisKeyConventions(string environment, string prefix = DefaultPrefix)
    {
        if (string.IsNullOrWhiteSpace(environment))
        {
            throw new ArgumentException("Environment cannot be null or whitespace.", nameof(environment));
        }

        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException("Prefix cannot be null or whitespace.", nameof(prefix));
        }

        ValidateSegment(environment, nameof(environment));
        ValidateSegment(prefix, nameof(prefix));

        Environment = environment.Trim().ToLowerInvariant();
        Prefix = prefix.Trim().ToLowerInvariant();
    }

    public string Format(string module, string category, params string[] identifiers)
    {
        return BuildKey(Prefix, Environment, module, category, identifiers);
    }

    public string Otp(string phone, string purpose)
    {
        ValidateSegment(phone, nameof(phone));
        ValidateSegment(purpose, nameof(purpose));
        return Format("identity", "otp", purpose, phone);
    }

    public string WsTicket(string jti)
    {
        ValidateSegment(jti, nameof(jti));
        return Format("identity", "ws-ticket", jti);
    }

    public string Impersonation(string code)
    {
        ValidateSegment(code, nameof(code));
        return Format("identity", "impersonation", code);
    }

    public string SlotLock(Guid pitchId, DateTime slotStartUtc)
    {
        if (pitchId == Guid.Empty)
        {
            throw new ArgumentException("PitchId cannot be empty.", nameof(pitchId));
        }

        var normalizedUtc = slotStartUtc.Kind == DateTimeKind.Utc
            ? slotStartUtc
            : slotStartUtc.ToUniversalTime();

        var timestamp = normalizedUtc.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture);
        return Format("booking", "lock", "slot", pitchId.ToString("N"), timestamp);
    }

    public string RateLimit(string category, string identifier)
    {
        ValidateSegment(category, nameof(category));
        ValidateSegment(identifier, nameof(identifier));
        return Format("ratelimit", category, identifier);
    }

    public static string BuildKey(string prefix, string environment, string module, string category, params string[] identifiers)
    {
        ValidateSegment(prefix, nameof(prefix));
        ValidateSegment(environment, nameof(environment));
        ValidateSegment(module, nameof(module));
        ValidateSegment(category, nameof(category));

        if (identifiers is null || identifiers.Length == 0)
        {
            throw new ArgumentException("At least one identifier must be provided for a Redis key.", nameof(identifiers));
        }

        var parts = new List<string>(4 + identifiers.Length)
        {
            prefix.Trim().ToLowerInvariant(),
            environment.Trim().ToLowerInvariant(),
            module.Trim().ToLowerInvariant(),
            category.Trim().ToLowerInvariant()
        };

        foreach (var id in identifiers)
        {
            ValidateSegment(id, nameof(identifiers));
            parts.Add(id.Trim());
        }

        return string.Join(':', parts);
    }

    public static void ValidateSegment(string segment, string paramName)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            throw new ArgumentException($"Redis key segment '{paramName}' cannot be null, empty, or whitespace.", paramName);
        }

        if (_invalidCharRegex.IsMatch(segment))
        {
            throw new ArgumentException($"Redis key segment '{paramName}' ('{segment}') contains illegal whitespace characters.", paramName);
        }
    }
}
