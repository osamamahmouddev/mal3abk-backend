using System.Security.Cryptography;
using System.Text;
using Malabak.Modules.Identity.Application.Common;
using Malabak.SharedKernel;
using Microsoft.Extensions.Options;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// HMAC-signed step-up token: <c>userId:scope:issuedUnix:bindingHash:signature</c>. The binding hash ties the token
/// to the user's security stamp at issue time; the stamp changes on password/phone change, which kills old tokens.
/// </summary>
public sealed class StepUpService : IStepUpService
{
    private const string _noBinding = "-";
    private static readonly TimeSpan _defaultMaxAge = TimeSpan.FromMinutes(5);

    private readonly byte[] _hmacKey;
    private readonly IClock _clock;

    public StepUpService(IOptions<StepUpOptions> options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        var secret = options.Value.SecretKey;
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        _hmacKey = Encoding.UTF8.GetBytes(secret);
        _clock = clock;
    }

    public string IssueStepUpToken(Guid userId, string scope, TimeSpan? validFor = null, string? binding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var issuedUnix = new DateTimeOffset(_clock.UtcNow).ToUnixTimeSeconds();
        var payload = $"{userId:N}:{scope.Trim().ToLowerInvariant()}:{issuedUnix}:{BindingHash(binding)}";
        return $"{payload}:{Sign(payload)}";
    }

    public bool ValidateStepUpToken(string stepUpToken, Guid expectedUserId, string expectedScope, TimeSpan? maxAge = null, string? binding = null)
    {
        if (string.IsNullOrWhiteSpace(stepUpToken) || string.IsNullOrWhiteSpace(expectedScope))
        {
            return false;
        }

        var parts = stepUpToken.Split(':');
        if (parts.Length != 5
            || !Guid.TryParse(parts[0], out var tokenUserId) || tokenUserId != expectedUserId
            || !string.Equals(parts[1], expectedScope.Trim(), StringComparison.OrdinalIgnoreCase)
            || !long.TryParse(parts[2], out var issuedUnix))
        {
            return false;
        }

        var payload = string.Join(':', parts[0], parts[1], parts[2], parts[3]);
        var expected = Encoding.UTF8.GetBytes(Sign(payload));
        var provided = Encoding.UTF8.GetBytes(parts[4].ToLowerInvariant());
        if (!CryptographicOperations.FixedTimeEquals(provided, expected))
        {
            return false;
        }

        if (binding is not null && !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(parts[3]), Encoding.UTF8.GetBytes(BindingHash(binding))))
        {
            return false;
        }

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedUnix).UtcDateTime;
        var nowUtc = _clock.UtcNow;

        // Reject tokens from the future (beyond 1 minute of skew) and tokens older than the allowed age.
        return issuedAt <= nowUtc.AddMinutes(1) && nowUtc - issuedAt <= (maxAge ?? _defaultMaxAge);
    }

    private string Sign(string payload) =>
        Convert.ToHexString(HMACSHA256.HashData(_hmacKey, Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    private static string BindingHash(string? binding) =>
        binding is null ? _noBinding : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(binding)))[..16].ToLowerInvariant();
}
